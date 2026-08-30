import { afterEach, describe, expect, it, vi } from "vitest";
import { handleAnnouncements, handleFeedback, handleLatestUpdate } from "../src/index";
import type { Env } from "../src/index";

const release = {
  id: 42,
  tag_name: "feedback-logs-2026-08",
  name: "CloudLight Blizzard Feedback Logs 2026-08",
  upload_url: "https://uploads.github.com/repos/owner/private-feedback/releases/42/assets{?name,label}",
  html_url: "https://github.com/owner/private-feedback/releases/tag/feedback-logs-2026-08",
};

function environment(): Env {
  return {
    FEEDBACK_RATE_LIMITER: { limit: vi.fn(async () => ({ success: true })) },
    GITHUB_OWNER: "owner",
    GITHUB_REPO: "private-feedback",
    GITHUB_TOKEN: "unit-test-placeholder",
    ANNOUNCEMENTS_URL: "https://example.test/announcements.json",
  } as unknown as Env;
}

function feedbackRequest(withLogs = true): Request {
  const form = new FormData();
  form.set("title", "测试错误\n不允许换行");
  form.set("description", "详细描述，不应包含日志正文");
  form.set("appVersion", "2.0.5");
  form.set("osVersion", "Windows 11 x64");
  form.set("contact", "tester@example.test");
  form.set("clientSubmissionId", "9f535e31-92c5-4d88-b40d-afdf82d980d8");
  if (withLogs)
    form.set("logs", new File([new Uint8Array([0x50, 0x4b, 0x03, 0x04])], "feedback.zip",
      { type: "application/zip" }));
  return new Request("https://worker.test/v1/feedback", { method: "POST", body: form });
}

function githubMock(options: {
  releaseExists?: boolean;
  issueStatus?: number;
  timeout?: boolean;
} = {}) {
  const calls: Array<{ url: string; method: string; body: BodyInit | null | undefined; headers: Headers }> = [];
  let issueBody = "";
  const mock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input);
    const method = init?.method ?? "GET";
    calls.push({ url, method, body: init?.body, headers: new Headers(init?.headers) });
    if (options.timeout) throw new DOMException("timed out", "TimeoutError");
    if (url.includes("/issues?") && method === "GET") return Response.json([]);
    if (url.includes("/releases/tags/") && method === "GET")
      return Response.json({ message: "draft releases are not returned here" }, { status: 404 });
    if (url.includes("/releases?per_page=100") && method === "GET")
      return Response.json(options.releaseExists ? [release] : []);
    if (url.endsWith("/releases") && method === "POST") return Response.json(release, { status: 201 });
    if (url.startsWith("https://uploads.github.com/") && method === "POST") {
      const name = new URL(url).searchParams.get("name")!;
      return Response.json({ id: 55, name, size: 4, browser_download_url: "https://github.test/private-asset" },
        { status: 201 });
    }
    if (url.endsWith("/issues") && method === "POST") {
      issueBody = JSON.parse(String(init?.body)).body;
      const status = options.issueStatus ?? 201;
      return status === 201
        ? Response.json({ number: 123, html_url: "https://github.test/private-issue", body: issueBody }, { status })
        : Response.json({ message: "failure" }, { status });
    }
    if (url.endsWith("/releases/assets/55") && method === "DELETE") return new Response(null, { status: 204 });
    throw new Error(`Unexpected GitHub request: ${method} ${url}`);
  });
  return { mock, calls, issueBody: () => issueBody };
}

afterEach(() => vi.unstubAllGlobals());

describe("POST /v1/feedback", () => {
  it("creates monthly draft release, uploads ZIP, then creates the linked issue", async () => {
    const github = githubMock();
    vi.stubGlobal("fetch", github.mock);
    const response = await handleFeedback(feedbackRequest(true), environment());
    const payload = await response.json() as { success: boolean; reportId: string; issueNumber: number };

    expect(response.status).toBe(201);
    expect(payload.success).toBe(true);
    expect(payload.reportId).toMatch(/^CB-\d{8}-[0-9A-F]{6}$/);
    expect(payload.issueNumber).toBe(123);
    expect(github.calls.map(call => `${call.method} ${call.url}`)).toEqual(expect.arrayContaining([
      expect.stringContaining("GET https://api.github.com/repos/owner/private-feedback/releases/tags/feedback-logs-"),
      "POST https://api.github.com/repos/owner/private-feedback/releases",
      expect.stringContaining("POST https://uploads.github.com/repos/owner/private-feedback/releases/42/assets?name="),
      "POST https://api.github.com/repos/owner/private-feedback/issues",
    ]));
    expect(github.issueBody()).toContain(payload.reportId);
    expect(github.issueBody()).toContain(`${payload.reportId}-logs.zip`);
    expect(github.issueBody()).toContain("日志大小：4 B");
    expect(github.issueBody()).not.toContain("PK");
    const createRelease = github.calls.find(call => call.method === "POST" && call.url.endsWith("/releases"))!;
    expect(JSON.parse(String(createRelease.body))).toMatchObject({ draft: true, prerelease: false });
    const upload = github.calls.find(call => call.url.startsWith("https://uploads.github.com/"))!;
    expect(upload.body).toBeInstanceOf(File);
    expect(upload.headers.get("content-type")).toBe("application/zip");
  });

  it("reuses the existing monthly release", async () => {
    const github = githubMock({ releaseExists: true });
    vi.stubGlobal("fetch", github.mock);
    const response = await handleFeedback(feedbackRequest(true), environment());
    expect(response.status).toBe(201);
    expect(github.calls.some(call => call.method === "POST" && call.url.endsWith("/releases"))).toBe(false);
    expect(github.calls.some(call => call.url.startsWith("https://uploads.github.com/"))).toBe(true);
  });

  it("deletes the uploaded asset and returns failure when issue creation fails", async () => {
    const github = githubMock({ releaseExists: true, issueStatus: 500 });
    vi.stubGlobal("fetch", github.mock);
    const response = await handleFeedback(feedbackRequest(true), environment());
    const payload = await response.json() as { success: boolean; error: string };
    expect(response.status).toBe(502);
    expect(payload).toEqual({ success: false, error: "github_unavailable" });
    expect(github.calls).toContainEqual(expect.objectContaining({
      method: "DELETE",
      url: "https://api.github.com/repos/owner/private-feedback/releases/assets/55",
    }));
  });

  it("returns a structured GitHub timeout", async () => {
    const github = githubMock({ timeout: true });
    vi.stubGlobal("fetch", github.mock);
    const response = await handleFeedback(feedbackRequest(true), environment());
    expect(response.status).toBe(504);
    expect(await response.json()).toEqual({ success: false, error: "github_timeout" });
  });

  it("creates an issue directly when no logs are attached", async () => {
    const github = githubMock();
    vi.stubGlobal("fetch", github.mock);
    const response = await handleFeedback(feedbackRequest(false), environment());
    expect(response.status).toBe(201);
    expect(github.calls.some(call => call.url.includes("/releases"))).toBe(false);
    expect(github.calls.some(call => call.url.startsWith("https://uploads.github.com/"))).toBe(false);
    expect(github.issueBody()).toContain("未附加运行日志");
  });

  it("rejects non-ZIP log data before calling GitHub", async () => {
    const github = githubMock();
    vi.stubGlobal("fetch", github.mock);
    const form = new FormData();
    form.set("title", "测试"); form.set("description", "描述");
    form.set("appVersion", "2.0.5"); form.set("osVersion", "Windows"); form.set("contact", "");
    form.set("logs", new File(["not zip"], "feedback.zip"));
    const response = await handleFeedback(new Request("https://worker.test/v1/feedback",
      { method: "POST", body: form }), environment());
    expect(response.status).toBe(400);
    expect(github.mock).not.toHaveBeenCalled();
  });
});

describe("GET /v1/announcements", () => {
  it("continues to proxy and validate announcements", async () => {
    const cache = { put: vi.fn(async () => undefined), match: vi.fn(async () => undefined) };
    vi.stubGlobal("caches", { open: vi.fn(async () => cache) });
    vi.stubGlobal("fetch", vi.fn(async () => Response.json({
      schemaVersion: 1,
      announcements: [{
        id: "2026-08-23-001", revision: 2, title: "公告", content: "正文",
        publishedAt: "2026-08-23T15:30:00+08:00", minVersion: "2.0.4", maxVersion: null, enabled: true,
      }],
    })));
    const response = await handleAnnouncements(environment());
    const payload = await response.json() as { announcements: unknown[] };
    expect(response.status).toBe(200);
    expect(payload.announcements).toHaveLength(1);
    expect(cache.put).toHaveBeenCalledOnce();
  });
});

describe("GET /v1/update/latest", () => {
  function latestRelease() {
    return {
      id: 99,
      tag_name: "v2.0.7",
      name: "CloudLight Blizzard 2.0.7",
      body: "修复更新检查",
      published_at: "2026-08-24T08:00:00Z",
      draft: false,
      prerelease: false,
      upload_url: "https://uploads.github.com/repos/Cloud-Light125/CloudLight-Blizzard/releases/99/assets{?name,label}",
      html_url: "https://github.com/Cloud-Light125/CloudLight-Blizzard/releases/tag/v2.0.7",
      assets: [{
        id: 100,
        name: "CloudLight-Blizzard-2.0.7-win-x64-Setup.exe",
        size: 123456,
        browser_download_url: "https://github.com/Cloud-Light125/CloudLight-Blizzard/releases/download/v2.0.7/CloudLight-Blizzard-2.0.7-win-x64-Setup.exe",
      }],
    };
  }

  it("returns only the latest release fields needed by the client", async () => {
    const cache = { put: vi.fn(async () => undefined), match: vi.fn(async () => undefined) };
    vi.stubGlobal("caches", { open: vi.fn(async () => cache) });
    const github = vi.fn(async (_input: RequestInfo | URL, init?: RequestInit) => {
      expect(new Headers(init?.headers).get("Authorization")).toBe("Bearer test-token");
      return Response.json(latestRelease());
    });
    vi.stubGlobal("fetch", github);

    const response = await handleLatestUpdate({ GITHUB_TOKEN: "test-token" });
    expect(response.status).toBe(200);
    expect(await response.json()).toEqual({
      ok: true,
      version: "2.0.7",
      latestVersion: "2.0.7",
      tag: "v2.0.7",
      tagName: "v2.0.7",
      name: "CloudLight Blizzard 2.0.7",
      notes: "修复更新检查",
      publishedAt: "2026-08-24T08:00:00Z",
      htmlUrl: "https://github.com/Cloud-Light125/CloudLight-Blizzard/releases/tag/v2.0.7",
      assets: [{
        name: "CloudLight-Blizzard-2.0.7-win-x64-Setup.exe",
        downloadUrl: "https://github.com/Cloud-Light125/CloudLight-Blizzard/releases/download/v2.0.7/CloudLight-Blizzard-2.0.7-win-x64-Setup.exe",
        size: 123456,
      }],
    });
    expect(github).toHaveBeenCalledOnce();
    expect(cache.put).toHaveBeenCalledOnce();
  });

  it("serves a cache hit without requesting GitHub", async () => {
    const cached = Response.json({ version: "2.0.7" });
    const cache = { put: vi.fn(), match: vi.fn(async () => cached) };
    vi.stubGlobal("caches", { open: vi.fn(async () => cache) });
    const github = vi.fn();
    vi.stubGlobal("fetch", github);

    const response = await handleLatestUpdate();
    expect(await response.json()).toEqual({ version: "2.0.7" });
    expect(github).not.toHaveBeenCalled();
  });

  it.each([
    [403, { "X-RateLimit-Remaining": "0", "X-RateLimit-Reset": "1787558400" }, "rate_limited", 503],
    [500, {}, "upstream_unavailable", 502],
  ])("maps GitHub HTTP %i to a structured failure", async (status, headers, error, expectedStatus) => {
    const cache = { put: vi.fn(), match: vi.fn(async () => undefined) };
    vi.stubGlobal("caches", { open: vi.fn(async () => cache) });
    vi.stubGlobal("fetch", vi.fn(async () => Response.json({ message: "rate limit exceeded" },
      { status, headers })));

    const response = await handleLatestUpdate();
    expect(response.status).toBe(expectedStatus);
    expect(await response.json()).toMatchObject({ success: false, error });
    expect(cache.put).not.toHaveBeenCalled();
  });
});
