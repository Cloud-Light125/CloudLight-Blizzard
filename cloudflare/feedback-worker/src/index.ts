export interface Env {
  FEEDBACK_RATE_LIMITER: RateLimit;
  ANNOUNCEMENTS_URL: string;
  GITHUB_OWNER: string;
  GITHUB_REPO: string;
  GITHUB_TOKEN?: string;
}

interface GitHubRelease {
  id: number;
  tag_name: string;
  name: string | null;
  body?: string | null;
  published_at?: string | null;
  draft?: boolean;
  prerelease?: boolean;
  upload_url: string;
  html_url: string;
  assets?: GitHubAsset[];
}

interface GitHubAsset {
  id: number;
  name: string;
  size: number;
  browser_download_url: string;
}

interface GitHubIssue {
  number: number;
  html_url: string;
  body?: string | null;
  pull_request?: unknown;
}

interface StoredFeedback {
  reportId: string;
  issueNumber: number;
  issueUrl: string;
}

type FeedbackErrorCode =
  | "github_unavailable"
  | "github_timeout"
  | "github_auth_failed"
  | "github_rate_limited"
  | "github_asset_upload_failed"
  | "github_issue_create_failed";

class GitHubOperationError extends Error {
  constructor(readonly code: FeedbackErrorCode, readonly httpStatus: number, technicalMessage: string) {
    super(technicalMessage);
  }
}

const MAX_LOG_BYTES = 25 * 1024 * 1024;
const MAX_REQUEST_BYTES = MAX_LOG_BYTES + 512 * 1024;
const GITHUB_TIMEOUT_MS = 15_000;
const ANNOUNCEMENT_CACHE_KEY = "https://cloudlight.internal-cache/v1/announcements";
const UPDATE_CACHE_KEY = "https://cloudlight.internal-cache/v1/update/latest";
const UPDATE_CACHE_TTL_SECONDS = 15 * 60;
const UPDATE_REPOSITORY_API = "https://api.github.com/repos/yundan125/CloudLight-Blizzard/releases/latest";
const GITHUB_API_VERSION = "2026-03-10";

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    const path = new URL(request.url).pathname;
    if (request.method === "GET" && path === "/v1/announcements")
      return handleAnnouncements(env);
    if (request.method === "GET" && path === "/v1/update/latest")
      return handleLatestUpdate();
    if (request.method === "POST" && path === "/v1/feedback")
      return handleFeedback(request, env);
    return json({ error: "not_found" }, 404);
  },
} satisfies ExportedHandler<Env>;

export async function handleAnnouncements(env: Env): Promise<Response> {
  const cache = await caches.open("cloudlight-announcements");
  const cacheKey = new Request(ANNOUNCEMENT_CACHE_KEY);
  try {
    const upstream = await fetch(env.ANNOUNCEMENTS_URL, {
      headers: { "User-Agent": "CloudLight-Feedback-Worker/2.0", Accept: "application/json" },
      cf: { cacheTtl: 300, cacheEverything: true },
    });
    if (!upstream.ok) throw new Error(`announcement upstream ${upstream.status}`);
    const payload: unknown = await upstream.json();
    if (!isAnnouncementDocument(payload)) throw new Error("invalid announcement document");
    const response = json(payload, 200, { "Cache-Control": "public, max-age=120" });
    await cache.put(cacheKey, response.clone());
    return response;
  } catch {
    const cached = await cache.match(cacheKey);
    if (cached) return new Response(cached.body, cached);
    return json({ schemaVersion: 1, announcements: [] }, 200, {
      "Cache-Control": "no-store", "X-Announcement-Source": "empty-fallback",
    });
  }
}

export async function handleLatestUpdate(): Promise<Response> {
  const cache = await caches.open("cloudlight-update");
  const cacheKey = new Request(UPDATE_CACHE_KEY);
  const cached = await cache.match(cacheKey);
  if (cached) return new Response(cached.body, cached);

  let upstream: Response;
  try {
    upstream = await fetch(UPDATE_REPOSITORY_API, {
      headers: {
        Accept: "application/vnd.github+json",
        "User-Agent": "CloudLight-Feedback-Worker/2.0",
        "X-GitHub-Api-Version": GITHUB_API_VERSION,
      },
      signal: AbortSignal.timeout(GITHUB_TIMEOUT_MS),
    });
  } catch (error) {
    return updateError(isTimeoutError(error) ? "timeout" : "network_unavailable",
      isTimeoutError(error) ? 504 : 502);
  }

  if (!upstream.ok) {
    const remaining = upstream.headers.get("x-ratelimit-remaining");
    const retryAfter = upstream.headers.get("retry-after");
    const resetAt = rateLimitResetIso(upstream.headers.get("x-ratelimit-reset"));
    const message = await githubMessage(upstream);
    console.error(`Update GitHub request failed: HTTP ${upstream.status}; remaining=${remaining ?? "n/a"}; ` +
      `reset=${resetAt ?? "n/a"}; retryAfter=${retryAfter ?? "n/a"}; message=${message || "n/a"}`);
    if (upstream.status === 429 ||
        (upstream.status === 403 && (remaining === "0" || /rate limit/i.test(message))))
      return updateError("rate_limited", 503, resetAt, retryAfter);
    if (upstream.status >= 500) return updateError("upstream_unavailable", 502);
    return updateError("upstream_http_error", 502);
  }

  let release: GitHubRelease;
  try { release = await upstream.json<GitHubRelease>(); }
  catch { return updateError("invalid_response", 502); }
  if (!isPublicRelease(release)) return updateError("invalid_response", 502);

  const payload = {
    version: release.tag_name.replace(/^v/i, ""),
    tag: release.tag_name,
    name: release.name ?? release.tag_name,
    notes: (release.body?.trim() ?? "").slice(0, 20_000),
    publishedAt: release.published_at ?? null,
    htmlUrl: release.html_url,
    assets: (release.assets ?? [])
      .filter(asset => asset.name === `CloudLight-Blizzard-${release.tag_name.replace(/^v/i, "")}-win-x64-Setup.exe`)
      .map(asset => ({
      name: asset.name,
      downloadUrl: asset.browser_download_url,
      size: asset.size,
      })),
  };
  const response = Response.json(payload, {
    headers: { "Cache-Control": `public, max-age=${UPDATE_CACHE_TTL_SECONDS}` },
  });
  await cache.put(cacheKey, response.clone());
  return response;
}

export async function handleFeedback(request: Request, env: Env): Promise<Response> {
  const contentType = request.headers.get("content-type") ?? "";
  if (!contentType.toLowerCase().startsWith("multipart/form-data;"))
    return json({ success: false, error: "invalid_content_type" }, 415);
  const declaredLength = Number(request.headers.get("content-length") ?? 0);
  if (declaredLength > MAX_REQUEST_BYTES)
    return json({ success: false, error: "payload_too_large" }, 413);

  const actor = `${request.headers.get("cf-connecting-ip") ?? "unknown"}|${
    (request.headers.get("user-agent") ?? "unknown").slice(0, 80)}`;
  const rate = await env.FEEDBACK_RATE_LIMITER.limit({ key: actor });
  if (!rate.success) return json({ success: false, error: "rate_limited" }, 429);

  let form: FormData;
  try { form = await request.formData(); }
  catch { return json({ success: false, error: "invalid_multipart" }, 400); }
  const title = sanitizeTitle(field(form, "title"));
  const description = sanitizeBodyText(field(form, "description"));
  const appVersion = sanitizeSingleLine(field(form, "appVersion"));
  const osVersion = sanitizeSingleLine(field(form, "osVersion"));
  const contact = sanitizeSingleLine(field(form, "contact"));
  const suppliedSubmissionId = field(form, "clientSubmissionId");
  const submissionId = isUuid(suppliedSubmissionId) ? suppliedSubmissionId.toLowerCase() : crypto.randomUUID();
  if (!title || title.length > 160 || !description || description.length > 10_000 ||
      !appVersion || appVersion.length > 100 || !osVersion || osVersion.length > 300 || contact.length > 200)
    return json({ success: false, error: "invalid_fields" }, 400);

  const logsValue = form.get("logs");
  const logs = logsValue instanceof File && logsValue.size > 0 ? logsValue : null;
  if (logs && logs.size > MAX_LOG_BYTES)
    return json({ success: false, error: "payload_too_large" }, 413);
  if (logs && !(await isZip(logs)))
    return json({ success: false, error: "invalid_zip" }, 400);
  if (!env.GITHUB_TOKEN || !env.GITHUB_OWNER || !env.GITHUB_REPO)
    return json({ success: false, error: "github_auth_failed" }, 503);

  try {
    const existing = await findExistingFeedback(env, submissionId);
    if (existing) return json({ success: true, ...existing }, 200);

    const now = new Date();
    const reportId = createReportId(now);
    let release: GitHubRelease | null = null;
    let asset: GitHubAsset | null = null;
    if (logs) {
      release = await findOrCreateMonthlyRelease(env, now);
      asset = await uploadLogAsset(env, release, logs, reportId);
    }

    try {
      const issue = await createFeedbackIssue(env, {
        reportId, submissionId, title, description, appVersion, osVersion, contact,
        createdAt: now, logs, release, asset,
      });
      return json({ success: true, reportId, issueNumber: issue.number, issueUrl: issue.html_url }, 201);
    } catch (error) {
      // A timeout can happen after GitHub accepted the Issue. Re-check the submission marker before cleanup/failure.
      try {
        const recovered = await findExistingFeedback(env, submissionId);
        if (recovered) return json({ success: true, ...recovered }, 200);
      } catch (recoveryError) {
        console.error(`Feedback Issue recovery check failed: ${safeErrorMessage(recoveryError)}`);
      }
      if (asset) await deleteAssetBestEffort(env, asset.id, "issue creation failed");
      throw error;
    }
  } catch (error) {
    const failure = normalizeGitHubError(error);
    console.error(`Feedback GitHub operation failed: ${failure.code}; ${failure.message}`);
    return json({ success: false, error: failure.code }, failure.httpStatus);
  }
}

async function findExistingFeedback(env: Env, submissionId: string): Promise<StoredFeedback | null> {
  const response = await githubFetch(env, `${repoApi(env)}/issues?state=all&per_page=30&sort=created&direction=desc`);
  requireGitHubOk(response, "github_unavailable", "list recent issues");
  const issues = await response.json<GitHubIssue[]>();
  const marker = submissionMarker(submissionId);
  const existing = issues.find(issue => !issue.pull_request && issue.body?.includes(marker));
  if (!existing) return null;
  const reportId = existing.body?.match(/反馈编号：\s*(CB-\d{8}-[0-9A-F]{6})/)?.[1];
  return reportId ? { reportId, issueNumber: existing.number, issueUrl: existing.html_url } : null;
}

async function findOrCreateMonthlyRelease(env: Env, now: Date): Promise<GitHubRelease> {
  const month = now.toISOString().slice(0, 7);
  const tag = `feedback-logs-${month}`;
  const found = await getReleaseByTag(env, tag);
  if (found) return found;

  const response = await githubFetch(env, `${repoApi(env)}/releases`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      tag_name: tag,
      name: `CloudLight Blizzard Feedback Logs ${month}`,
      body: "CloudLight Blizzard private feedback log container.",
      draft: true,
      prerelease: false,
    }),
  });
  if (response.ok) return response.json<GitHubRelease>();
  if (response.status === 409 || response.status === 422) {
    const raced = await getReleaseByTag(env, tag);
    if (raced) return raced;
  }
  throwForGitHubStatus(response, "github_unavailable", "create monthly release");
}

async function getReleaseByTag(env: Env, tag: string): Promise<GitHubRelease | null> {
  const response = await githubFetch(env, `${repoApi(env)}/releases/tags/${encodeURIComponent(tag)}`);
  if (response.status === 404) {
    // GitHub's tag endpoint is documented for published releases. Authenticated listing also includes drafts.
    const list = await githubFetch(env, `${repoApi(env)}/releases?per_page=100&page=1`);
    requireGitHubOk(list, "github_unavailable", "list draft releases");
    const releases = await list.json<GitHubRelease[]>();
    return releases.find(release => release.tag_name === tag) ?? null;
  }
  requireGitHubOk(response, "github_unavailable", "get monthly release");
  return response.json<GitHubRelease>();
}

async function uploadLogAsset(env: Env, release: GitHubRelease, logs: File,
    reportId: string): Promise<GitHubAsset> {
  const uploadBase = validateUploadUrl(release.upload_url);
  let assetName = `${reportId}-logs.zip`;
  for (let attempt = 0; attempt < 3; attempt++) {
    const response = await githubFetch(env, `${uploadBase}?name=${encodeURIComponent(assetName)}`, {
      method: "POST",
      headers: { "Content-Type": "application/zip" },
      body: logs,
    });
    if (response.status === 201) return response.json<GitHubAsset>();
    if (response.status === 422) {
      assetName = `${reportId}-logs-${secureHex(2)}.zip`;
      continue;
    }
    await cleanupAssetByNameBestEffort(env, release.id, assetName);
    throwForGitHubStatus(response, "github_asset_upload_failed", "upload release asset");
  }
  throw new GitHubOperationError("github_asset_upload_failed", 502, "release asset name collision");
}

async function createFeedbackIssue(env: Env, data: {
  reportId: string;
  submissionId: string;
  title: string;
  description: string;
  appVersion: string;
  osVersion: string;
  contact: string;
  createdAt: Date;
  logs: File | null;
  release: GitHubRelease | null;
  asset: GitHubAsset | null;
}): Promise<GitHubIssue> {
  const body = [
    "## CloudLight Blizzard 用户反馈",
    "",
    `反馈编号：${data.reportId}`,
    `版本：${data.appVersion}`,
    `系统：${data.osVersion}`,
    `提交时间：${formatChinaTime(data.createdAt)}`,
    "",
    "### 联系方式",
    "",
    data.contact || "未提供",
    "",
    "### 错误描述",
    "",
    data.description,
    "",
    "### 日志",
    "",
    data.asset && data.release && data.logs
      ? [
          "已附加运行日志：",
          "",
          `\`${data.asset.name}\``,
          "",
          `日志大小：${formatBytes(data.asset.size || data.logs.size)}`,
          "",
          `[Feedback Logs ${data.createdAt.toISOString().slice(0, 7)} Release](${data.release.html_url})`,
        ].join("\n")
      : "未附加运行日志。",
    "",
    submissionMarker(data.submissionId),
  ].join("\n");
  const response = await githubFetch(env, `${repoApi(env)}/issues`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ title: `[用户反馈] ${data.title}`, body }),
  });
  requireGitHubOk(response, "github_issue_create_failed", "create feedback issue");
  return response.json<GitHubIssue>();
}

async function cleanupAssetByNameBestEffort(env: Env, releaseId: number, assetName: string): Promise<void> {
  try {
    const response = await githubFetch(env, `${repoApi(env)}/releases/${releaseId}/assets?per_page=100`);
    if (!response.ok) return;
    const assets = await response.json<GitHubAsset[]>();
    const asset = assets.find(item => item.name === assetName);
    if (asset) await deleteAssetBestEffort(env, asset.id, "failed upload cleanup");
  } catch (error) {
    console.error(`Release asset lookup cleanup failed: ${safeErrorMessage(error)}`);
  }
}

async function deleteAssetBestEffort(env: Env, assetId: number, reason: string): Promise<void> {
  try {
    const response = await githubFetch(env, `${repoApi(env)}/releases/assets/${assetId}`, { method: "DELETE" });
    if (response.status !== 204 && response.status !== 404)
      console.error(`Release asset cleanup failed (${reason}): HTTP ${response.status}`);
  } catch (error) {
    console.error(`Release asset cleanup failed (${reason}): ${safeErrorMessage(error)}`);
  }
}

async function githubFetch(env: Env, url: string, init: RequestInit = {}): Promise<Response> {
  try {
    return await fetch(url, {
      ...init,
      headers: {
        Authorization: `Bearer ${env.GITHUB_TOKEN}`,
        Accept: "application/vnd.github+json",
        "User-Agent": "CloudLight-Feedback-Worker/2.0",
        "X-GitHub-Api-Version": GITHUB_API_VERSION,
        ...init.headers,
      },
      signal: AbortSignal.timeout(GITHUB_TIMEOUT_MS),
    });
  } catch (error) {
    if (isTimeoutError(error))
      throw new GitHubOperationError("github_timeout", 504, "GitHub request timed out");
    throw new GitHubOperationError("github_unavailable", 502, `GitHub network failure: ${safeErrorMessage(error)}`);
  }
}

function requireGitHubOk(response: Response, fallback: FeedbackErrorCode, operation: string): void {
  if (!response.ok) throwForGitHubStatus(response, fallback, operation);
}

function throwForGitHubStatus(response: Response, fallback: FeedbackErrorCode, operation: string): never {
  const remaining = response.headers.get("x-ratelimit-remaining");
  if (response.status === 429 || (response.status === 403 && remaining === "0"))
    throw new GitHubOperationError("github_rate_limited", 503, `${operation}: rate limited`);
  if (response.status === 401 || response.status === 403)
    throw new GitHubOperationError("github_auth_failed", 503, `${operation}: authentication/permission ${response.status}`);
  if (response.status >= 500)
    throw new GitHubOperationError("github_unavailable", 502, `${operation}: GitHub HTTP ${response.status}`);
  throw new GitHubOperationError(fallback, 502, `${operation}: GitHub HTTP ${response.status}`);
}

function normalizeGitHubError(error: unknown): GitHubOperationError {
  return error instanceof GitHubOperationError ? error
    : new GitHubOperationError("github_unavailable", 502, safeErrorMessage(error));
}

function repoApi(env: Env): string {
  return `https://api.github.com/repos/${encodeURIComponent(env.GITHUB_OWNER)}/${encodeURIComponent(env.GITHUB_REPO)}`;
}

function validateUploadUrl(template: string): string {
  const raw = template.split("{")[0];
  const url = new URL(raw);
  if (url.protocol !== "https:" || url.hostname !== "uploads.github.com")
    throw new GitHubOperationError("github_asset_upload_failed", 502, "invalid GitHub upload_url");
  return url.toString();
}

function field(form: FormData, name: string): string {
  const value = form.get(name);
  return typeof value === "string" ? value.trim() : "";
}

function sanitizeTitle(value: string): string {
  return value.replace(/[\u0000-\u001f\u007f]+/g, " ").replace(/\s+/g, " ").trim();
}

function sanitizeSingleLine(value: string): string {
  return value.replace(/[\u0000-\u001f\u007f]+/g, " ").replace(/\s+/g, " ").trim();
}

function sanitizeBodyText(value: string): string {
  return value.replace(/[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f]/g, "").trim();
}

async function isZip(file: File): Promise<boolean> {
  // Only the four-byte slice is materialized. The full File is passed directly to GitHub fetch.
  const signature = new Uint8Array(await file.slice(0, 4).arrayBuffer());
  return signature.length === 4 && signature[0] === 0x50 && signature[1] === 0x4b &&
    ((signature[2] === 0x03 && signature[3] === 0x04) ||
     (signature[2] === 0x05 && signature[3] === 0x06) ||
     (signature[2] === 0x07 && signature[3] === 0x08));
}

function isPublicRelease(value: unknown): value is GitHubRelease {
  if (!value || typeof value !== "object") return false;
  const release = value as Partial<GitHubRelease>;
  return typeof release.tag_name === "string" && /^v?\d+\.\d+(?:\.\d+){0,2}$/.test(release.tag_name) &&
    typeof release.html_url === "string" &&
    release.html_url.startsWith("https://github.com/yundan125/CloudLight-Blizzard/releases/tag/") &&
    release.draft !== true && release.prerelease !== true && Array.isArray(release.assets) &&
    release.assets.every(asset => typeof asset.name === "string" && typeof asset.size === "number" &&
      typeof asset.browser_download_url === "string" &&
      asset.browser_download_url.startsWith(
        "https://github.com/yundan125/CloudLight-Blizzard/releases/download/"));
}

async function githubMessage(response: Response): Promise<string> {
  try {
    const payload = await response.clone().json<{ message?: unknown }>();
    return typeof payload.message === "string" ? payload.message.slice(0, 200) : "";
  } catch { return ""; }
}

function rateLimitResetIso(value: string | null): string | null {
  if (!value || !/^\d+$/.test(value)) return null;
  const date = new Date(Number(value) * 1000);
  return Number.isNaN(date.getTime()) ? null : date.toISOString();
}

function updateError(error: string, status: number, resetAt: string | null = null,
    retryAfter: string | null = null): Response {
  const headers: Record<string, string> = { "X-Update-Error": error };
  if (retryAfter && /^\d+$/.test(retryAfter)) headers["Retry-After"] = retryAfter;
  return json({ success: false, error, resetAt }, status, headers);
}

function isAnnouncementDocument(value: unknown): value is { schemaVersion: 1; announcements: unknown[] } {
  if (!value || typeof value !== "object") return false;
  const doc = value as { schemaVersion?: unknown; announcements?: unknown };
  if (doc.schemaVersion !== 1 || !Array.isArray(doc.announcements) || doc.announcements.length > 100) return false;
  return doc.announcements.every(item => {
    if (!item || typeof item !== "object") return false;
    const a = item as Record<string, unknown>;
    return typeof a.id === "string" && a.id.length > 0 && a.id.length <= 100 &&
      typeof a.revision === "number" && Number.isInteger(a.revision) && a.revision > 0 &&
      typeof a.title === "string" && a.title.length > 0 && a.title.length <= 200 &&
      typeof a.content === "string" && a.content.length <= 20_000 &&
      typeof a.publishedAt === "string" && !Number.isNaN(Date.parse(a.publishedAt)) &&
      typeof a.enabled === "boolean" && (a.minVersion === null || typeof a.minVersion === "string") &&
      (a.maxVersion === null || typeof a.maxVersion === "string");
  });
}

function createReportId(date: Date): string {
  const day = date.toISOString().slice(0, 10).replaceAll("-", "");
  return `CB-${day}-${secureHex(3)}`;
}

function secureHex(byteCount: number): string {
  const random = crypto.getRandomValues(new Uint8Array(byteCount));
  return Array.from(random, byte => byte.toString(16).padStart(2, "0")).join("").toUpperCase();
}

function submissionMarker(submissionId: string): string {
  return `<!-- cloudlight-submission-id: ${submissionId} -->`;
}

function isUuid(value: string): boolean {
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(value);
}

function formatChinaTime(date: Date): string {
  return new Intl.DateTimeFormat("zh-CN", {
    timeZone: "Asia/Shanghai", year: "numeric", month: "2-digit", day: "2-digit",
    hour: "2-digit", minute: "2-digit", hour12: false,
  }).format(date).replaceAll("/", "-");
}

function formatBytes(bytes: number): string {
  return bytes >= 1024 * 1024 ? `${(bytes / 1024 / 1024).toFixed(1)} MB`
    : bytes >= 1024 ? `${(bytes / 1024).toFixed(1)} KB` : `${bytes} B`;
}

function isTimeoutError(error: unknown): boolean {
  return error instanceof DOMException && (error.name === "TimeoutError" || error.name === "AbortError") ||
    error instanceof Error && (error.name === "TimeoutError" || error.name === "AbortError");
}

function safeErrorMessage(error: unknown): string {
  return error instanceof Error ? `${error.name}: ${error.message}`.slice(0, 300) : "unknown error";
}

function json(value: unknown, status = 200, headers: Record<string, string> = {}): Response {
  return Response.json(value, { status, headers: { "Cache-Control": "no-store", ...headers } });
}
