# ChildNotes × Home Assistant 喂养记录集成

将 aiji 后台（ChildNotes API）的当日吃奶喂养记录接入 Home Assistant：HA 通过 HACS 安装本仓库的自定义集成后，定时（默认 10 分钟）拉取喂养数据，自动生成统计传感器，可在仪表盘展示今日喂养次数/总量/最近喂养时间/预计下次喂养，并查看完整记录明细。

## 1. 整体架构

```
┌──────────────┐   HTTPS (X-Api-Key)   ┌────────────────────────────────────┐
│ Home         │ ────────────────────► │ 云服务器 (Ubuntu)                    │
│ Assistant    │  GET /api/ha/feeding  │  Caddy :443（自动 HTTPS 证书）       │
│ (HACS 自定义  │ ◄──────────────────── │   └─ childnotes.hacloud.asia        │
│  集成)        │   JSON {state,msg,data}│      └─ reverse_proxy 127.0.0.1:8080│
└──────────────┘   默认每 10 分钟拉取   │  ChildNotes.Api (systemd :8080)     │
                                       │   └─ PostgreSQL child_notes         │
                                       └────────────────────────────────────┘
```

- 路由/证书：复用现有 Caddy 反代（`childnotes.hacloud.asia` 整域反代到 `127.0.0.1:8080`），**新增 `/api/ha/*` 路径无需修改 Caddy 配置**。
- 鉴权：独立于 App 的 JWT 体系，使用 `X-Api-Key` 请求头，密钥与用户邮箱的映射配置在服务端。
- 业务口径：喂养"当日"= 北京时间自然日（与 App 端"今日记录"一致）。

## 2. 后端接口说明

### 2.1 接口定义

| 项目 | 内容 |
|---|---|
| 路径 | `GET /api/ha/feeding` |
| 查询参数 | `date`（可选，`yyyy-MM-dd`，缺省为北京今天） |
| 请求头 | `X-Api-Key: <密钥>` |
| 成功 | HTTP 200，`{"state":"000000","msg":"success","data":{...}}` |
| 密钥无效 | HTTP 401，`{"state":"000520","code":"INVALID_API_KEY",...}` |
| 服务异常 | HTTP 500，统一 JSON 信封（不会返回非 JSON 页面） |

### 2.2 响应 data 结构

```json
{
  "date": "2026-10-27",
  "babyName": "小极",
  "serverTime": "2026-10-27T15:30:00+08:00",
  "records": [
    {
      "id": "记录ID",
      "time": "2026-10-27T08:30:00+08:00",
      "type": "breast",
      "side": "left",
      "durationMin": 15,
      "amountMl": null,
      "note": "备注"
    }
  ],
  "summary": {
    "count": 8,
    "totalMl": 520,
    "breastCount": 3,
    "bottleCount": 4,
    "expressedCount": 1,
    "totalDurationMin": 45,
    "lastFeedTime": "2026-10-27T15:00:00+08:00",
    "lastFeedType": "bottle"
  }
}
```

- `type`：`breast` 母乳亲喂 / `bottle` 奶瓶 / `expressed` 吸出母乳
- 所有时间均为 ISO 8601 带 `+08:00` 时区偏移，HA 可直接作为 `timestamp` 使用
- `amountMl` 亲喂为 `null`；`durationMin` 无时长场景为 `null`

### 2.3 验证命令

```bash
# 服务器本机直连
curl -H "X-Api-Key: <密钥>" http://127.0.0.1:8080/api/ha/feeding

# 外网经 Caddy + HTTPS
curl -H "X-Api-Key: <密钥>" https://childnotes.hacloud.asia/api/ha/feeding

# 无效密钥应返回 401
curl -i https://childnotes.hacloud.asia/api/ha/feeding
```

## 3. 部署步骤

### 3.1 生成 API Key 并配置到服务器

在云服务器上生成一个随机密钥（不要用弱密钥）：

```bash
openssl rand -hex 24
```

编辑生产配置（跨版本共享，不会随升级丢失）：

```bash
sudo nano /opt/childnotes/shared/appsettings.Production.json
```

追加/修改 `Ha` 节点，**key 为 API 密钥，value 为该用户在 App 中注册的邮箱**：

```json
{
  "Ha": {
    "ApiKeys": {
      "<openssl 生成的密钥>": "你在 App 里注册的邮箱@example.com"
    }
  }
}
```

> 可以配置多个密钥对应不同用户；将整个 `ApiKeys` 置空对象 `{}` 即可停用 HA 接入。

### 3.2 重启服务并验证

```bash
sudo systemctl restart childnotes-api
curl -H "X-Api-Key: <密钥>" http://127.0.0.1:8080/api/ha/feeding
```

### 3.3 Caddy 配置

现有配置已覆盖新接口（整域反代），如尚未配置过，在 `/etc/caddy/Caddyfile` 中追加：

```caddy
childnotes.hacloud.asia {
    reverse_proxy 127.0.0.1:8080
}
```

```bash
sudo caddy validate --config /etc/caddy/Caddyfile   # 检查语法
sudo systemctl reload caddy                          # 平滑重载，Caddy 自动续期证书
```

用 2.3 节的外网 curl 命令验证 HTTPS 链路。

### 3.4 后端版本要求

本接口自后端 **v0.5.5+**（含 `HaController`）可用，按标准流程 `sudo childnotes-deploy vX.X.X` 升级即可。

## 4. HACS 安装

1. HA 左侧栏 → **HACS** → 右上角 **⋮** → **自定义存储库**（Custom repositories）
2. 存储库地址填：`https://github.com/KleinPan/ChildNotes`
3. 类别选：**集成**（Integration）
4. 点击 **添加** → 在列表中找到 **ChildNotes Feeding** → **下载**
5. 重启 Home Assistant

> HACS 通过仓库根目录的 `hacs.json` + `custom_components/childnotes/` 识别集成，后续更新走 HACS 正常升级流程（版本号见 `manifest.json` 的 `version` 字段）。

## 5. HA 界面配置

1. **设置** → **设备与服务** → **添加集成** → 搜索 **ChildNotes 喂养记录**
2. 填写：
   - 后台接口地址：`https://childnotes.hacloud.asia`
   - API 密钥：3.1 节生成的密钥
   - 拉取间隔：默认 `10` 分钟（可自定义 1–1440 分钟）
3. 提交时集成会立即调用一次接口验证；成功后自动创建以下实体（同属设备 **ChildNotes 喂养**）：

| 实体 | 说明 | 类型/单位 |
|---|---|---|
| `sensor.xxx_今日喂养次数` | 当日喂养总次数 | total |
| `sensor.xxx_今日喂养总量` | 当日瓶喂+吸出母乳合计奶量 | mL / total |
| `sensor.xxx_最近喂养时间` | 最近一次喂养的时间点 | timestamp |
| `sensor.xxx_预计下次喂养` | 按"今日相邻喂养间隔均值"估算的下一次时间 | timestamp |
| `sensor.xxx_喂养记录明细` | 当日完整记录列表（含时间/类型/侧别/奶量/时长/备注） | attributes |

### 异常行为约定

- 接口临时不可用（超时/网络错误/5xx）：实体**保留上一次成功数据**继续展示，`last_sync_success=false`、`last_error` 属性标记异常；恢复后自动续上。
- API 密钥失效：HA 在集成页提示"重新配置"，更新密钥即可。
- 跨日切换：后端按北京自然日返回，零点后的下一个拉取周期自动切到新一天的数据。

## 6. 仪表盘卡片模板

### 6.1 汇总卡片（Entities 卡片，开箱即用）

```yaml
type: entities
title: 喂养记录
entities:
  - entity: sensor.childnotes_feed_count
    name: 今日喂养次数
  - entity: sensor.childnotes_feed_amount
    name: 今日喂养总量
  - entity: sensor.childnotes_last_feed
    name: 最近喂养
    format: time
  - entity: sensor.childnotes_next_feed
    name: 预计下次喂养
    format: relative
```

> 实体 ID 前缀以实际为准（在"开发者工具 → 状态"中搜索 `childnotes` 即可找到）。

### 6.2 记录明细卡片（Markdown + Jinja，当日完整列表）

```yaml
type: markdown
title: 今日喂养明细
content: >-
  {% set recs = state_attr('sensor.childnotes_feed_records', 'records') or [] %}
  {% if recs | length == 0 %}
  今日暂无喂养记录
  {% else %}
  | 时间 | 类型 | 奶量 | 时长 | 备注 |
  |---|---|---|---|---|
  {% for r in recs | sort(attribute='time') %}
  | {{ (r.time | string).split('T')[1][:5] }} | {{ r.type }}
    {%- if r.side %}（{{ {'left':'左','right':'右','both':'双侧'}[r.side] }}）{% endif -%}
  | {{ r.amount_ml ~ ' mL' if r.amount_ml else '-' }}
  | {{ r.duration_min ~ ' min' if r.duration_min else '-' }}
  | {{ r.note or '' }} |
  {% endfor %}
  {% endif %}
```

### 6.3 喂养提醒自动化（可选）

```yaml
automation:
  - alias: "喂养时间提醒"
    trigger:
      - platform: time
        at: sensor.childnotes_next_feed
    action:
      - service: notify.persistent_notification
        data:
          title: 喂养提醒
          message: >
            距上次喂养已约
            {{ state_attr('sensor.childnotes_next_feed', 'avg_interval_min') | round(0) }} 分钟，可能需要喂奶了。
```

## 7. 测试清单（全链路）

1. **接口层**：2.3 节三条 curl（无 Key 401 / 错 Key 401 / 正确 Key 返回当日数据）。
2. **后端自动化测试**：`ChildNotes.Tests.HaFeedApiTests`（鉴权、空日、明细+汇总、指定日期）。
3. **路由层**：外网 `https://childnotes.hacloud.asia/api/ha/feeding` 与本机直连返回一致。
4. **集成层**：HACS 安装 → 配置流验证（错误地址/错误密钥的报错提示）→ 实体生成。
5. **端到端**：App 中新增一条吃奶记录 → 等一个拉取周期（≤10 分钟）→ HA 实体数值变化。
6. **异常演练**：临时 `sudo systemctl stop childnotes-api` → 实体保留旧数据且 `last_sync_success=false` → 恢复服务后数据续上。

## 8. 故障排查

| 现象 | 排查 |
|---|---|
| 配置流报"无法连接" | 服务器上先跑 2.3 节 curl；检查 DNS/443/Caddy；`sudo journalctl -u caddy -n 50` |
| 配置流报"API 密钥无效" | 核对 `appsettings.Production.json` 的 `Ha:ApiKeys`（key/邮箱均需精确）与重启状态 |
| 实体数据不更新 | HA 日志搜 `childnotes`；确认 `last_error` 属性；App 记录是否落在"北京今天" |
| HACS 看不到集成 | 确认自定义存储库类别选了"集成"；仓库根有 `hacs.json` 与 `custom_components/childnotes/manifest.json` |

## 9. 相关代码

| 文件 | 说明 |
|---|---|
| [HaController.cs](../ChildNotes.Backend/ChildNotes.Api/Controllers/HaController.cs) | `/api/ha/feeding` 接口 + X-Api-Key 鉴权 |
| [HaFeedService.cs](../ChildNotes.Backend/ChildNotes.Infrastructure/Services/HaFeedService.cs) | 喂养数据查询与汇总 |
| [HaOptions.cs](../ChildNotes.Backend/ChildNotes.Core/Config/HaOptions.cs) | API Key 配置模型 |
| [HaFeedApiTests.cs](../ChildNotes.Backend/ChildNotes.Tests/HaFeedApiTests.cs) | 接口集成测试 |
| `custom_components/childnotes/` | HACS 集成本体（manifest / config_flow / coordinator / sensor） |
| [bootstrap.sh](../scripts/deploy/bootstrap.sh) | 全新安装已包含 `Ha` 配置段 |
