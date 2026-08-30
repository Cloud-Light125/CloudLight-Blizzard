# CloudLight Blizzard feedback Worker

该 Worker 只作为 API Gateway：

- `GET /v1/announcements`：读取并校验公开的 `announcements.json`，短时缓存；上游失败时返回最近有效缓存，没有缓存则返回空公告。
- `POST /v1/feedback`：校验反馈并生成 reportId；有日志时上传到私有反馈仓库的当月 Draft Release Asset，随后创建私有 Issue；无日志时直接创建 Issue。
- `FEEDBACK_RATE_LIMITER`：公开反馈接口的网络/客户端级限流。

Worker 不使用 R2、D1、KV 或 Cron 长期保存反馈。日志 ZIP 不写入 Git Contents，因此不会进入 Git commit 历史。任何 Secret 都不要写进源码、`wrangler.toml`、JSON、`.env` 或 Git 仓库。

## GitHub 私有仓库和 Token

1. 创建或确认私有仓库：`Cloud-Light125/CloudLight-Blizzard-Feedback`，并至少初始化一个默认分支（例如创建 README）。所有 Release、Asset 和 Issue 只创建在这个反馈仓库，不操作 CloudLight-Blizzard 主仓库的正式 Release。
2. 创建或更新 fine-grained PAT：
   - Repository access：`Only selected repositories` → `CloudLight-Blizzard-Feedback`
   - Repository permissions：`Issues: Read and write`
   - Repository permissions：`Contents: Read and write`
   - Repository permissions：`Metadata: Read`
   - 不需要 Actions、Administration、Workflows、Pull requests、Secrets 或 Deployments。
3. 当前 Worker 已存在名为 `GITHUB_TOKEN` 的远端 Secret。不要读取或导出它。若现有 PAT 没有 `Contents: Read and write`，在 GitHub 更新权限；如果 GitHub 不允许修改，则重新创建同仓库范围的 PAT，然后在交互式终端覆盖远端 Secret：

   ```powershell
   npx wrangler secret put GITHUB_TOKEN
   ```

   不要把 Token 写在命令行参数中，也不要发给 Codex。

## 普通公开配置

`wrangler.toml` 中以下值不是 Secret，可以直接保存：

```toml
ANNOUNCEMENTS_URL = "https://raw.githubusercontent.com/Cloud-Light125/CloudLight-Blizzard/main/announcements.json"
GITHUB_OWNER = "Cloud-Light125"
GITHUB_REPO = "CloudLight-Blizzard-Feedback"
```

Rate Limiting binding 保持每 60 秒 10 次；`namespace_id = "1001"` 必须在当前 Cloudflare 账号内保持唯一。

## Release 与 Issue 结构

- 每月复用一个 Draft Release：`feedback-logs-YYYY-MM`。
- Release 名称：`CloudLight Blizzard Feedback Logs YYYY-MM`。
- Asset 默认名称：`CB-YYYYMMDD-XXXXXX-logs.zip`；极低概率重名时追加安全随机后缀，绝不覆盖现有 Asset。
- Worker 使用 GitHub Release 返回的 `upload_url`，移除 `{?name,label}` 后追加编码的 `name`，并以 `application/zip` 直接发送原始 File。
- Asset 上传成功而 Issue 创建失败时，Worker 会 BestEffort 删除对应 Asset；反馈整体返回失败。
- 每个 GitHub 请求有 15 秒 timeout。GitHub 没有成功保存 Issue 时，客户端不会显示“反馈已提交”。
- Issue body 仅包含反馈 metadata、描述和 Asset/Release 标识，不包含 ZIP、Base64 或完整日志。

## 本地检查

```powershell
npm install
npm run typecheck
npm test
npx wrangler deploy --dry-run
```

`--dry-run` 只验证构建和 bindings，不正式发布。只有用户后续明确要求时才可执行 `npx wrangler deploy`。

桌面端通过公开的 `CloudServiceConfiguration.DefaultBaseUrl` 访问已部署 Worker；本机 `settings.json` 的非秘密字段 `CloudServiceBaseUrl` 仅用于兼容和测试覆盖。
