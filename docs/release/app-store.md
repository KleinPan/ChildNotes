# App Store / 应用商店发布清单

本清单面向 ChildNotes Android/iOS 发布准备，重点覆盖国内 Android 应用商店、隐私合规、物料和版本发布流程。

## 发布前必查

| 类别 | 检查项 |
|---|---|
| 账号资质 | 企业/个人开发者账号、软著、ICP、APP 备案 |
| 应用信息 | 应用名称、简介、图标、截图、隐私政策、用户协议 |
| 包信息 | 正式包名、版本号、签名证书、权限声明 |
| 合规 | 儿童信息、隐私弹窗、AI 建议免责声明、第三方 SDK 清单 |
| 技术 | Release 构建、崩溃日志、网络环境、后端域名、升级策略 |

## Android 重点

- 确认正式包名不再使用默认 `com.CompanyName.ChildNotes`。
- 固定签名证书并安全保存 keystore。
- 核对国内商店对软著、ICP备案、APP 备案和权限说明的要求。
- 隐私政策需列明儿童信息、设备信息、网络请求、上传内容和第三方服务。

## iOS 重点

- 权限弹窗文案需解释清楚使用目的。
- 截图、年龄分级、隐私标签和 AI 免责声明需保持一致。
- 如果 AI 分析涉及健康建议，需要明确“仅供参考，不构成医疗诊断”。

## 历史来源

完整历史清单保存在 [`../archive/engineering/app-store-launch-checklist.md`](../archive/engineering/app-store-launch-checklist.md)。
