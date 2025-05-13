# MediatR重构进度

## 已完成

- [x] AI/OCR：AutoOcrRequestHandler 已实现，原Controller功能已迁移到MediatR Handler，支持Bot图片消息自动OCR。
- [x] Download/DownloadPhotoRequestHandler 已实现，原DownloadPhotoController功能已迁移到MediatR Handler，实现Bot图片消息保存。
- [x] Download/DownloadAudioRequestHandler 已实现，原DownloadAudioController功能已迁移到MediatR Handler，实现Bot音频消息保存。
- [x] Download/DownloadVideoRequestHandler 已实现，原DownloadVideoController功能已迁移到MediatR Handler，实现Bot视频消息保存。
- [x] AI/ASR/AutoAsrRequestHandler 已实现，原AutoASRController功能已迁移到MediatR Handler，实现Bot音视频自动ASR转写。
- [x] AI/LLM/GeneralLlmRequestHandler 已实现，原GeneralLLMController功能已迁移到MediatR Handler，实现Bot LLM 对话。
- [x] Bilibili/BiliMessageHandler 已实现，原BiliMessageController功能已迁移到MediatR Handler
- [x] Bilibili/BiliVideoHandler 已实现，视频处理逻辑完整迁移
- [x] Bilibili/BiliOpusHandler 已实现，动态处理逻辑完整迁移

## 进行中

- [ ] Download（其余子模块）
- [ ] Manage
- [ ] Search
- [ ] Storage

> 每完成一个模块的Handler重构，持续更新本进度文档。
