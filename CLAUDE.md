# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 项目概述

本科 .NET C# 大作业 — 复刻 EasyDataset 项目，构建 Alpaca 数据集生成系统。
用户导入文档（.md、.txt），系统调用 LLM 自动生成 instruction-input-output 格式的问答对，
最终导出为 JSONL 文件用于大模型微调。

## 技术栈

- .NET 10 (ASP.NET Core MVC + Razor Views)
- EF Core 9 + Pomelo.EntityFrameworkCore.MySql (MySQL 8.0+)
- LLM API：MiniMax OpenAI-compatible 接口（`api.minimax.chat/v1`），两步法 Prompt（先问题后答案）
- 存储：MySQL（本地 localhost:3306，数据库 databuilder）

## 架构设计

```
┌─────────────────────────────────────────────────┐
│                   前端 UI                        │
│     文档上传 / 项目管理 / QA预览编辑 / 导出       │
└────────────────────┬────────────────────────────┘
                     │ HTTP + MVC
┌────────────────────▼────────────────────────────┐
│              ASP.NET Core MVC                    │
│                                                  │
│  ┌──────────┐  ┌──────────┐  ┌───────────────┐  │
│  │ 文档导入  │  │ 文档解析  │  │  问答对生成    │  │
│  │ (上传)   │→│ (分段)   │→│  (LLM调用)    │  │
│  └──────────┘  └──────────┘  └───────┬───────┘  │
│                                       │          │
│  ┌──────────┐  ┌──────────┐  ┌───────▼───────┐  │
│  │ 数据集导出│←│ 编辑问答  │←│  结果存储      │  │
│  │ (JSONL)  │  │ (人工)   │  │  (MySQL)      │  │
│  └──────────┘  └──────────┘  └───────────────┘  │
└─────────────────────────────────────────────────┘
```

### 核心模块

- **DocumentParser**: 解析 `.md` 和 `.txt` 文件，三种策略（Heading/Paragraph/FixedLength）
- **LLMService**: 调用 MiniMax API 生成问答对，两步法（GenerateQuestionsAsync → GenerateAnswersAsync），含重试和限流
- **AlpacaExporter**: 将生成的问答对导出为标准 JSONL（每行一个 `{instruction, input, output}` 对象）
- **ProjectController**: 项目管理 CRUD + 自定义 Prompt 设置

### 数据模型

```
Project 1──* Document 1──* Chunk 1──* QAPair
```

- **Project**: 名称、描述、QuestionPrompt、AnswerPrompt、创建/更新时间
- **Document**: 文件名、内容、DocumentStatus 枚举（Uploaded/Parsing/Parsed/Generating/Done）
- **Chunk**: 所属文档、Sequence、文本内容、分段策略
- **QAPair**: instruction、input、output、Type、QualityScore、Answered 标记

## 版本管理

本项目采用语义化版本管理（Semantic Versioning）。

格式：`vMAJOR.MINOR.PATCH`

| 版本号 | 变更类型 |
|--------|----------|
| MAJOR | 不兼容的 API 变更、架构重构 |
| MINOR | 向后兼容的功能新增 |
| PATCH | 向后兼容的 Bug 修复 |

### 发版流程

```bash
# 提交代码后打 tag
git tag -a v1.0.0 -m "描述本次变更"
git push origin v1.0.0
```

### 发版前检查

- `dotnet build` 0 Warning 0 Error
- 所有页面可正常访问，无 404/500 错误
- LLM API 可正常调用
- 数据库迁移无待处理的变更

## 开发命令

```bash
# 构建
dotnet build

# 运行（监听 5067 端口）
dotnet run --project DataBuilder.Api

# 数据库迁移
dotnet ef migrations add <MigrationName> --project DataBuilder.Api
dotnet ef database update --project DataBuilder.Api
```

## 注意事项

- `.env` 文件包含 API Token（MINIMAX_TOKEN），已被 `.gitignore` 排除，切勿提交
- `.env.example` 为环境变量模板，不含真实 Token，可安全提交
- MiniMax API 使用 `api.minimax.chat/v1` 域名（Token Plan 的 OpenAI 兼容端点），注意和旧域名 `api.minimaxi.com` 区分
- JSON 序列化必须使用 `[JsonPropertyName]` 标注，因为手动 `Serialize()` 不继承 ASP.NET 全局 camelCase 策略
- HttpClient BaseAddress 必须以 `/` 结尾，否则 URI 拼接时会丢失路径段
