# DataBuilder 开发文档

> Alpaca 数据集构建系统 — 本科 .NET C# 大作业
>
> 参考项目: [EasyDataset](https://github.com/ConardLi/easy-dataset) (AGPL-3.0)
>
> 生成日期: 2025-05-25

---

## 目录

1. [项目概述](#1-项目概述)
2. [功能范围](#2-功能范围)
3. [技术选型](#3-技术选型)
4. [系统架构](#4-系统架构)
5. [数据模型设计](#5-数据模型设计)
6. [API 接口设计](#6-api-接口设计)
7. [LLM Prompt 设计方案](#7-llm-prompt-设计方案)
8. [前端页面设计](#8-前端页面设计)
9. [开发计划](#9-开发计划)
10. [附录](#附录)

---

## 1. 项目概述

### 1.1 项目背景

大语言模型（LLM）微调需要高质量的结构化数据集，Alpaca 格式（instruction-input-output 三元组）是使用最广泛的微调数据格式之一。本项目 **DataBuilder** 复刻 EasyDataset 的核心流程：**文档导入 → 智能分段 → LLM 生成问答对 → JSONL 导出**。

### 1.2 Alpaca 数据格式

```json
{
  "instruction": "请解释什么是依赖注入，并说明它的优点。",
  "input": "",
  "output": "依赖注入（Dependency Injection, DI）是一种设计模式..."
}
```

- **instruction**: 任务描述或问题
- **input**: 可选的上下文信息（可为空字符串）
- **output**: 期望的回答

导出为 JSONL 格式（每行一个 JSON 对象），可直接用于 LLaMA-Factory 等微调框架。

---

## 2. 功能范围

### 2.1 本项目实现的功能

| 功能模块 | 描述 |
|----------|------|
| 项目管理 | 创建/删除/列表查看项目 |
| 文档导入 | 上传 .md / .txt 文件（限制 10MB），存储原始内容 |
| 文档解析分段 | 支持按标题(Heading)、段落(Paragraph)、固定长度(FixedLength)三种策略 |
| 问答对生成 | 两步法：先生成问题列表，再逐问题生成答案 |
| 问答类型选择 | 事实型(Factoid) / 推理型(Reasoning) / 总结型(Summary) |
| 问题/答案编辑 | 生成后用户在界面编辑或删除问题文本和答案文本 |
| 自定义 Prompt | 项目级别自定义"问题生成专家"和"答案生成专家"的 System Prompt |
| 数据集导出 | 导出为 Alpaca 标准 JSONL 文件 |

### 2.2 EasyDataset 有但本项目不做

| 不做 | 原因 |
|------|------|
| PDF/DOCX/EPUB 解析 | 复杂度高，依赖重，本科大作业不需要 |
| 多轮对话 / 图像问答 / 数据蒸馏 | 业务复杂度远超课程作业范围 |
| GA Pair 体裁-受众配对 / 标签树 | 增加不必要的复杂度 |
| 模型评估系统 / Hugging Face 上传 / 资源监控 | 独立子系统，体量过大 |
| 桌面客户端 / 多语言 / 多模型对比 | Web 版 + 中文 + 仅 Minimax 即可 |

---

## 3. 技术选型

| 层面 | 技术 | 为什么 |
|------|------|--------|
| 框架 | ASP.NET Core MVC (.NET 10) | 课程要求 C#，MVC 模式自带 Razor 视图，无需额外前端框架 |
| 数据库 | MySQL 8.0 + EF Core + Pomelo | MySQL 比 SQLite 更适合课程答辩演示（有服务进程，有连接配置可展示）；Pomelo 是 EF Core 下最成熟的 MySQL 提供者 |
| 前端 | Razor Views + Bootstrap 5 | MVC 模板自带，零额外依赖，Bootstrap 保证 UI 整洁 |
| LLM 调用 | HttpClient + System.Text.Json | Minimax 兼容 OpenAI 接口，直接 HTTP 调用最可控，避免 SDK 兼容性问题 |
| 文件处理 | 内置（仅 .md/.txt） | 无需第三方解析库 |

### 3.1 关键 NuGet 包

核心依赖集中在 `DataBuilder.Core`：
- `Pomelo.EntityFrameworkCore.MySql` — MySQL EF Core 提供者
- `Microsoft.EntityFrameworkCore` / `Microsoft.EntityFrameworkCore.Design` — ORM 框架

`DataBuilder.Api` 引用上述 + ASP.NET Core MVC 框架包即可。

### 3.2 Minimax API 配置

| 配置项 | 值 |
|--------|-----|
| Base URL | `https://api.minimaxi.com/v1` |
| 认证方式 | `Authorization: Bearer <MINIMAX_TOKEN>` |
| 默认模型 | `MiniMax-M2.5` |
| 注意 | 不走流式(stream=false)；不支持 OpenAI JSON Mode，需在 Prompt 中强制要求 JSON 输出 |

> Minimax Token Plan 的 API Key 与普通 Key 不同，需确认使用正确的 Token Plan 密钥，配置在 `.env` 文件的 `MINIMAX_TOKEN` 中。

---

## 4. 系统架构

### 4.1 整体架构

```
┌──────────────────────────────────────────────────┐
│             Razor Views (前端 UI)                  │
│   项目列表 / 文档上传 / 问答对预览 / JSONL 导出    │
└─────────────────────┬────────────────────────────┘
                      │ HTTP
┌─────────────────────▼────────────────────────────┐
│           ASP.NET Core MVC Controllers            │
│  HomeController / ProjectController /             │
│  DocumentController / QAController /              │
│  ExportController                                 │
└─────────────────────┬────────────────────────────┘
                      │ 依赖注入
┌─────────────────────▼────────────────────────────┐
│           DataBuilder.Core (Services)              │
│  DocumentParser  →  文本分段策略                   │
│  LLMService      →  Minimax API (两步法)           │
│  AlpacaExporter   →  JSONL 格式化                  │
└──────────────────────┬───────────────────────────┘
                       │ EF Core
┌──────────────────────▼───────────────────────────┐
│                MySQL 数据库                        │
│   Projects / Documents / Chunks / QAPairs         │
└──────────────────────────────────────────────────┘
```

### 4.2 核心数据流

```
用户上传 .md/.txt → 存入 Documents 表
         ↓
   DocumentParser 解析分段 → 存入 Chunks 表
         ↓
   第一步：LLMService.GenerateQuestionsAsync() 逐 chunk 生成问题 → 存入 QAPairs (Answered=false)
         ↓
   用户在界面编辑/审核问题列表
         ↓
   第二步：LLMService.GenerateAnswerAsync() 逐问题生成答案 → 更新 QAPairs (Answered=true)
         ↓
   用户在界面编辑/审核问答对
         ↓
   AlpacaExporter 导出 JSONL → 浏览器下载
```

> **为什么两步法而非一步法？** 问题和答案有独立的 Prompt 调优空间，用户可在两步之间审核编辑问题，质量远高于一步生成。

### 4.3 目录结构

```
DataBuilder/
├── DataBuilder.slnx
├── DataBuilder.Api/                  # MVC Web 应用
│   ├── Controllers/                  # 仅 HomeController 已实现，其他待第二阶段
│   ├── Views/                        # Razor 视图（按控制器分子目录）
│   ├── wwwroot/                      # 静态资源
│   ├── Program.cs                    # 应用入口 + DI 配置
│   └── appsettings.json
├── DataBuilder.Core/                 # 业务逻辑类库
│   ├── Entities/                     # Project.cs / Document.cs / Chunk.cs / QAPair.cs
│   ├── DTOs/                         # ChatCompletionDto.cs（OpenAI 兼容请求/响应体）
│   ├── Interfaces/                   # IDocumentParser / ILLMService / IAlpacaExporter
│   ├── Services/                     # DocumentParser / LLMService / AlpacaExporter
│   └── AppDbContext.cs               # EF Core DbContext（含索引配置）
└── .env                              # API Token（已 gitignore）
```

---

## 5. 数据模型设计

### 5.1 ER 图

```
┌──────────┐ 1──* ┌──────────┐ 1──* ┌──────────┐ 1──* ┌──────────┐
│  Project │      │ Document │      │  Chunk   │      │  QAPair  │
├──────────┤      ├──────────┤      ├──────────┤      ├──────────┤
│ Id (PK)  │      │ Id (PK)  │      │ Id (PK)  │      │ Id (PK)  │
│ Name     │      │ProjectId │      │DocumentId│      │ ChunkId  │
│ Desc     │      │ FileName │      │ Sequence │      │Instruct. │
│QuesPrompt│      │ Content  │      │TextCont. │      │ Input    │
│AnswPrompt│      │ Status   │      │ Strategy │      │ Output   │
│CreatedAt │      │CreatedAt │      │CreatedAt │      │ Type     │
│UpdatedAt │      └──────────┘      └──────────┘      │QualitySc.│
└──────────┘                                          │ Answered │
                                                      │CreatedAt │
                                                      └──────────┘
```

### 5.2 表结构详细说明

**Projects (项目表)**

| 字段 | 类型 | 约束 | 说明 |
|------|------|------|------|
| Id | INT | PK, AUTO_INCREMENT | 主键 |
| Name | VARCHAR(200) | NOT NULL | 项目名称 |
| Description | VARCHAR(1000) | NULL | 项目描述 |
| QuestionPrompt | TEXT | NULL | 自定义问题生成 System Prompt（null 使用默认模板） |
| AnswerPrompt | TEXT | NULL | 自定义答案生成 System Prompt（null 使用默认模板） |
| CreatedAt | DATETIME | NOT NULL | 创建时间 |
| UpdatedAt | DATETIME | NOT NULL | 更新时间 |

**Documents (文档表)**

| 字段 | 类型 | 约束 | 说明 |
|------|------|------|------|
| Id | INT | PK, AUTO_INCREMENT | 主键 |
| ProjectId | INT | FK → Projects.Id, CASCADE | 所属项目 |
| FileName | VARCHAR(500) | NOT NULL | 原始文件名 |
| Content | LONGTEXT | NOT NULL | 文档原始内容 |
| Status | VARCHAR(50) | DEFAULT 'Uploaded' | 状态: Uploaded → Parsed → GenerationDone |
| CreatedAt | DATETIME | NOT NULL | 创建时间 |

**Chunks (文本片段表)**

| 字段 | 类型 | 约束 | 说明 |
|------|------|------|------|
| Id | INT | PK, AUTO_INCREMENT | 主键 |
| DocumentId | INT | FK → Documents.Id, CASCADE | 所属文档 |
| Sequence | INT | NOT NULL | 在文档中的序号（从 0 开始） |
| TextContent | TEXT | NOT NULL | 文本内容 |
| Strategy | VARCHAR(50) | DEFAULT 'Paragraph' | 分段策略: Heading / Paragraph / FixedLength |
| CreatedAt | DATETIME | NOT NULL | 创建时间 |

**QAPairs (问答对表)**

| 字段 | 类型 | 约束 | 说明 |
|------|------|------|------|
| Id | INT | PK, AUTO_INCREMENT | 主键 |
| ChunkId | INT | FK → Chunks.Id, CASCADE | 来源文本片段 |
| Instruction | TEXT | NOT NULL | Alpaca instruction（问题文本） |
| Input | TEXT | NULL | Alpaca input（可选上下文） |
| Output | TEXT | NOT NULL | Alpaca output（答案文本，默认为空字符串） |
| Type | VARCHAR(50) | DEFAULT 'Factoid' | 问答类型: Factoid / Reasoning / Summary |
| QualityScore | INT | DEFAULT 3 | 质量评分 1-5 |
| Answered | BOOLEAN | DEFAULT FALSE | 是否已生成答案（两步法标记） |
| CreatedAt | DATETIME | NOT NULL | 生成时间 |

### 5.3 数据库索引策略

所有外键均建立索引（定义在 `AppDbContext.OnModelCreating()` 中）：

| 索引 | 表 | 列 | 用途 |
|------|----|----|------|
| IX_Documents_ProjectId | Documents | ProjectId | 按项目查询文档 |
| IX_Chunks_DocumentId | Chunks | DocumentId | 按文档查询分段 |
| IX_QAPairs_ChunkId | QAPairs | ChunkId | 按分段查询问答对 |

---

## 6. API 接口设计

### 6.1 路由总览

| 方法 | 路由 | 请求参数 / 说明 |
|------|------|-----------------|
| GET | `/` | 首页（项目列表） |
| GET/POST | `/Project/Create` | 创建项目（GET: 表单页 / POST: form name+description） |
| GET | `/Project/{id}` | 项目详情（文档列表 + 操作入口） |
| POST | `/Project/Delete/{id}` | 删除项目 |
| GET | `/Project/Settings/{id}` | 项目设置页（自定义 Prompt 编辑） |
| POST | `/Project/UpdatePrompt/{id}` | 更新 Prompt（form: questionPrompt, answerPrompt） |
| GET/POST | `/Document/Upload` | 上传文档（GET: ?projectId / POST: multipart projectId+file） |
| POST | `/Document/Parse/{id}` | 解析分段（form: strategy, chunkSize） |
| GET | `/Document/Chunks/{id}` | 查看分段结果 |
| POST | `/QA/GenerateQuestions/{documentId}` | 第一步：生成问题（form: qaType, countPerChunk） |
| POST | `/QA/GenerateAnswers/{documentId}` | 第二步：为未回答问题生成答案 |
| GET | `/QA/Preview/{documentId}` | 预览问答对列表（含编辑/删除入口） |
| POST | `/QA/EditQuestion/{id}` | 编辑问题文本（form: instruction） |
| POST | `/QA/EditAnswer/{id}` | 编辑答案文本（form: output） |
| POST | `/QA/Delete/{id}` | 删除单条问答对 |
| POST | `/QA/UpdateQuality/{id}` | 修改质量评分（form: score） |
| GET | `/Export/Download/{projectId}` | 导出 JSONL（Content-Disposition: attachment） |

### 6.2 关键接口说明

**POST /Document/Upload** — 文件上传与格式限制

- Content-Type: `multipart/form-data`
- 字段: `projectId` (int), `file` (.md 或 .txt)
- 文件大小限制: 最大 10MB（在 `Program.cs` 中配置 `RequestSizeLimit`）
- 扩展名校验: 仅允许 `.md` 和 `.txt`，其他格式返回错误
- 处理: 读取内容 → 存入 Documents 表 (Status = "Uploaded") → 重定向项目详情

**POST /QA/GenerateQuestions/{documentId}** — 第一步问题生成

- 字段: `qaType` (Factoid/Reasoning/Summary), `countPerChunk` (默认 3)
- 逻辑: 获取所有 Chunks → 逐 chunk 调用 `LLMService.GenerateQuestionsAsync()` → 存入 QAPairs (Answered=false, Output="")
- LLM 重试: 最多 3 次，指数退避（2^attempt 秒），失败后该 chunk 的问题为空

**POST /QA/GenerateAnswers/{documentId}** — 第二步答案生成

- 逻辑: 获取所有 Answered=false 的 QAPair → 逐条调用 `LLMService.GenerateAnswerAsync(chunkText, instruction)` → 写入 Output，设 Answered=true → 更新 Document.Status 为 "GenerationDone"
- 同样适用 3 次重试机制

---

## 7. LLM Prompt 设计方案

### 7.1 两步法设计

系统采用与 EasyDataset 一致的两步法，参考其 `lib/llm/prompts/question.js` 和 `lib/llm/prompts/answer.js`：

1. **第一步 — 问题生成**: LLM 阅读文本片段，生成 N 个指定类型的问题，以 JSON 数组输出
2. **第二步 — 答案生成**: 针对每个问题，LLM 基于原文生成答案，以纯文本输出

对比一步法，两步法的优势在于：每个步骤有独立的 Prompt 优化空间，且用户可以在两步之间审核和编辑问题文本，最终数据质量更高。

### 7.2 Prompt 模板结构

两个步骤的 System Prompt 均采用统一结构：

```
# Role: <角色定义>
## Profile — 角色身份和专业能力描述
## Skills  — 核心技能（3-5 条）
## Workflow — 分步执行流程
## Constraints — 关键约束（互不冲突，覆盖 4-7 条）
## OutputFormat — 严格输出格式（仅问题生成步骤）
```

### 7.3 第一步：问题生成 Prompt

**System Prompt 核心要素**（完整模板见 `LLMService.BuildDefaultQuestionSystemPrompt()`）：
- 角色: "文本问题生成专家"
- 关键约束: 严格依据原文不得虚构、问题自包含可脱离上下文理解、禁用"报告中提到"等引用性表述、输出恰好 N 个问题
- 输出格式: 严格 JSON 字符串数组 `["问题1", "问题2", ...]`

**User Prompt 结构**: 给定文本内容 + 要求生成 N 个指定类型的问题

**输出解析**: 正则 `\[[\s\S]*?\]` 提取 JSON 数组 → `System.Text.Json` 反序列化 → 失败则回退按行拆分

### 7.4 第二步：答案生成 Prompt

**System Prompt 核心要素**（完整模板见 `LLMService.BuildDefaultAnswerSystemPrompt()`）：
- 角色: "微调数据集生成专家"
- 关键约束: 答案必须严格基于参考内容不得使用外部知识、不得出现引用性表述、答案应充分详细适合微调训练

**User Prompt 结构**: 参考内容 + 问题文本 → 要求基于参考内容生成答案

**输出处理**: 返回纯文本，直接 Trim 后存入 `QAPair.Output`

### 7.5 问答类型与参数

| 类型 | 中文名 | Temperature | 说明 |
|------|--------|-------------|------|
| Factoid | 事实型 | 0.7 | 从文本中提取事实性信息生成问答 |
| Reasoning | 推理型 | 0.5 | 基于文本内容进行推理分析 |
| Summary | 总结型 | 0.7 | 对文本内容进行归纳总结 |

### 7.6 自定义 Prompt

项目支持通过 `Project.QuestionPrompt` / `Project.AnswerPrompt` 自定义 System Prompt。LLMService 的优先级逻辑为：

```
项目自定义 Prompt（不为 null/空时使用） > LLMService 内置默认模板
```

### 7.7 Minimax API 调用要点

| 要点 | 说明 |
|------|------|
| 不走流式 | 设置 `stream=false`，避免 Minimax SSE 格式兼容差异 |
| 强制 JSON | Minimax 不支持 `response_format: json_object`，依赖 Prompt 约束 |
| 后处理容错 | 正则提取 JSON 数组 + 按行拆分回退 |
| 重试机制 | 最多 3 次，指数退避 `delay = 2^attempt` 秒 |
| MaxTokens | 问题生成: `count × 300`；答案生成: `1500` |
| HTTP 超时 | 120 秒 |

---

## 8. 前端页面设计

### 8.1 页面清单

| 页面 | 路由 | 核心功能 |
|------|------|----------|
| 首页 | `/` | 项目列表 + 新建/删除入口 |
| 创建项目 | `/Project/Create` | 表单：名称 + 描述 |
| 项目详情 | `/Project/{id}` | 文档列表 + 上传/解析/生成/导出按钮 |
| 项目设置 | `/Project/Settings/{id}` | 编辑自定义 QuestionPrompt 和 AnswerPrompt |
| 上传文档 | `/Document/Upload/{projectId}` | 文件选择（限 .md/.txt，10MB）+ 上传 |
| 分段预览 | `/Document/Chunks/{id}` | 分段结果展示，含序号和策略标注 |
| 问答对预览 | `/QA/Preview/{documentId}` | 问题列表（可编辑/删除）+ 生成答案按钮 + 答案编辑 + 质量评分 |
| 错误页 | `/Home/Error` | 通用错误提示 |

### 8.2 UI 风格

Bootstrap 5（MVC 模板自带），简洁清爽，适合课程答辩演示。

---

## 9. 开发计划

### 第一阶段：基础搭建（已完成）

- [x] 创建 .NET 解决方案和项目结构
- [x] 配置 EF Core + MySQL（含外键级联删除和索引）
- [x] 实现 4 个数据实体（Project / Document / Chunk / QAPair），含 QuestionPrompt、AnswerPrompt、Answered 字段
- [x] 实现 ILLMService 接口 + LLMService（Minimax API 调用，含两步法、默认 Prompt 模板、3 次重试）
- [x] 实现 IDocumentParser 接口 + DocumentParser（Heading/Paragraph/FixedLength 三种策略）
- [x] 实现 IAlpacaExporter 接口 + AlpacaExporter（JSONL 导出）
- [x] 配置 .env 加载 + DI 注册

### 第二阶段：MVC 后端（待完成）

- [ ] 实现 ProjectController（Index/Create/Delete + Settings/UpdatePrompt Views）
- [ ] 实现 DocumentController（Upload/Parse/Chunks Views + 文件大小/格式校验）
- [ ] 实现 QAController（两步生成 + 编辑/删除 + Preview View）
- [ ] 实现 ExportController（Download）
- [ ] 编写所有 Razor Views
- [ ] 数据库迁移 + 种子数据

### 第三阶段：联调测试

- [ ] 端到端流程测试
- [ ] 自定义 Prompt 配置测试
- [ ] LLM 输出质量抽样检查
- [ ] 错误处理完善（网络异常、JSON 解析失败等）

---

## 附录

### A. 开发环境配置

```bash
# 1. 确保 MySQL 已运行，创建数据库
mysql -u root -p -e "CREATE DATABASE IF NOT EXISTS databuilder CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;"

# 2. 配置 appsettings.json 中的连接字符串

# 3. API Key 已在 .env 文件中配置（MINIMAX_TOKEN），启动时自动加载

# 4. 数据库迁移
cd DataBuilder.Api
dotnet ef migrations add InitialCreate
dotnet ef database update

# 5. 启动
dotnet run
```

### B. EasyDataset 参考信息

- GitHub: https://github.com/ConradLi/easy-dataset (v1.7.3, AGPL-3.0)
- 技术栈: Next.js 14 + React 18 + Prisma + SQLite + Vercel AI SDK
- Prompt 模板结构: 问题生成 (`lib/llm/prompts/question.js`) + 答案生成 (`lib/llm/prompts/answer.js`)
- 数据生成采用两步法 + GA Pair 体裁-受众配对，本项目继承了其两步法核心设计
