// project-stats.js
// ECharts 5 张图渲染 + 主题联动。
// 依赖：ECharts 5.5.1（全局变量 echarts，由 _Layout.cshtml 通过 CDN 引入）。
//
// 设计要点：
//  - IIFE 隔离作用域，避免污染全局。
//  - DOMContentLoaded 后并发拉 5 个端点。
//  - 主题横柱图点击 -> 记录 currentTopic -> 重拉其他 4 张（带 topic 参数）。
//  - 「清除筛选」按钮点击 -> currentTopic = null -> 重拉 5 张。
//  - resize 防抖 150ms。
//  - beforeunload 时 dispose 所有 chart，避免内存泄漏。
//  - 拉取失败时 console.error + 在图表容器内显示「数据加载失败」。

(function () {
    'use strict';

    // Bootstrap 调色板（与 site.css 主题色一致）。
    var COLORS = {
        primary:  '#0d6efd',
        success:  '#198754',
        info:     '#0dcaf0',
        warning:  '#ffc107',
        danger:   '#dc3545',
        purple:   '#6f42c1',
        pink:     '#d63384',
        teal:     '#20c997',
        orange:   '#fd7e14',
        gray:     '#6c757d'
    };
    var PIE_PALETTE = [
        COLORS.primary, COLORS.success, COLORS.info, COLORS.warning,
        COLORS.danger, COLORS.purple, COLORS.pink, COLORS.teal,
        COLORS.orange, COLORS.gray
    ];

    var HIGHLIGHT = {
        borderColor: '#000',
        borderWidth: 2,
        shadowBlur: 8,
        shadowColor: 'rgba(0,0,0,0.4)'
    };

    var state = {
        projectId: 0,
        currentTopic: null,           // 当前联动主题
        currentController: null,      // 当前 refreshAll 的 AbortController（用于取消旧请求）
        charts: {},                   // id -> echarts instance
        resizeTimer: null,
        initialized: false            // 图表是否已初始化（折叠容器首次展开后才 init）
    };

    // ============================================================
    // 工具函数
    // ============================================================

    function $(id) { return document.getElementById(id); }

    function showError(containerId, msg) {
        var el = $(containerId);
        if (!el) return;
        el.innerHTML = '<div class="d-flex align-items-center justify-content-center h-100 text-danger">' +
            '<i class="bi bi-exclamation-triangle me-2"></i>' +
            '<span>' + (msg || '数据加载失败') + '</span></div>';
    }

    function clearError(containerId) {
        var el = $(containerId);
        if (!el) return;
        el.innerHTML = '';
    }

    // ECharts 原生 loading：SVG spinner + 半透明蒙层。文字/颜色与 Bootstrap 主色对齐。
    var LOADING_OPTION = {
        text: '加载中...',
        color: COLORS.primary,
        textColor: '#666',
        maskColor: 'rgba(255, 255, 255, 0.6)',
        fontSize: 14
    };

    function showLoading(chartId) {
        var c = state.charts[chartId];
        if (c && !c.isDisposed()) {
            // skipTopic 时复用「过滤中...」文案，强化联动语义
            c.showLoading(chartId === 'chart-topic' ? LOADING_OPTION : {
                text: '过滤中...',
                color: COLORS.primary,
                textColor: '#666',
                maskColor: 'rgba(255, 255, 255, 0.6)',
                fontSize: 14
            });
        }
    }

    function hideLoading(chartId) {
        var c = state.charts[chartId];
        if (c && !c.isDisposed()) c.hideLoading();
    }

    function fetchJson(url, signal) {
        return fetch(url, { credentials: 'same-origin', signal: signal })
            .then(function (resp) {
                if (!resp.ok) throw new Error('HTTP ' + resp.status);
                return resp.json();
            })
            .catch(function (err) {
                // 透传 AbortError，让调用方识别并静默处理
                if (err && err.name === 'AbortError') throw err;
                throw err;
            });
    }

    function withTopic(base, topic) {
        return base + (topic ? '&topic=' + encodeURIComponent(topic) : '');
    }

    function updateClearButton() {
        var btn = $('filter-clear');
        if (!btn) return;
        if (state.currentTopic) {
            btn.classList.remove('d-none');
            btn.innerHTML = '<i class="bi bi-x-circle"></i> 清除筛选：' + state.currentTopic;
        } else {
            btn.classList.add('d-none');
        }
    }

    // ============================================================
    // 端点 URL 构建
    // ============================================================

    function urlQAType()      { return '/Stats/QAType?projectId=' + state.projectId; }
    function urlQuality()     { return '/Stats/QualityScore?projectId=' + state.projectId; }
    function urlTopic()       { return '/Stats/Topic?projectId=' + state.projectId; }
    function urlDocTop()      { return '/Stats/DocumentTop?projectId=' + state.projectId; }
    function urlAnswer()      { return '/Stats/AnswerStatus?projectId=' + state.projectId; }

    // ============================================================
    // 图表初始化（5 个）
    // ============================================================

    function initChart(id, option) {
        clearError(id);
        var dom = $(id);
        if (!dom) return null;
        if (!state.charts[id]) {
            var inst = echarts.init(dom);
            state.charts[id] = inst;
        }
        state.charts[id].setOption(option, true);
        return state.charts[id];
    }

    function baseTooltip() {
        return { trigger: 'item', confine: true };
    }

    function baseTooltipAxis() {
        return { trigger: 'axis', axisPointer: { type: 'shadow' }, confine: true };
    }

    function makeQATypeOption(data) {
        return {
            tooltip: baseTooltip(),
            legend: { bottom: 0, type: 'scroll' },
            series: [{
                name: '类型',
                type: 'pie',
                radius: ['40%', '70%'],
                avoidLabelOverlap: true,
                itemStyle: { borderRadius: 4, borderColor: '#fff', borderWidth: 2 },
                label: { formatter: '{b}\n{d}%' },
                data: data.map(function (d) {
                    return {
                        name: d.name || '(空)',
                        value: d.value || 0,
                        itemStyle: { color: COLORS.primary }
                    };
                })
            }]
        };
    }

    function makeQualityOption(data) {
        // 补齐缺失的 1-5 分为 0
        var map = {};
        (data || []).forEach(function (d) { map[d.score] = d.count; });
        var categories = [1, 2, 3, 4, 5];
        var values = categories.map(function (s) { return map[s] || 0; });
        return {
            tooltip: baseTooltipAxis(),
            grid: { top: 30, left: 40, right: 20, bottom: 30 },
            xAxis: { type: 'category', data: categories.map(function (s) { return s + ' 分'; }) },
            yAxis: { type: 'value', minInterval: 1 },
            series: [{
                name: '质量分',
                type: 'bar',
                data: values.map(function (v, i) {
                    var color = [COLORS.danger, COLORS.warning, COLORS.info, COLORS.primary, COLORS.success][i];
                    return { value: v, itemStyle: { color: color } };
                }),
                barMaxWidth: 50,
                label: { show: true, position: 'top' }
            }]
        };
    }

    function makeTopicOption(data) {
        // 横柱图：value 在 x 轴，category 在 y 轴；按 count 倒序排。
        // 降序 + yAxis 默认从下往上 = 顶部显示最大值。
        var sorted = (data || []).slice().sort(function (a, b) { return b.value - a.value; });
        var names = sorted.map(function (d) { return d.name || '(空)'; });
        var values = sorted.map(function (d) { return d.value || 0; });

        return {
            tooltip: baseTooltipAxis(),
            grid: { top: 10, left: 100, right: 30, bottom: 30 },
            xAxis: { type: 'value', minInterval: 1 },
            yAxis: { type: 'category', data: names, axisLabel: { interval: 0 } },
            series: [{
                name: '主题',
                type: 'bar',
                data: values.map(function (v, i) {
                    var isCurrent = state.currentTopic && names[i] === state.currentTopic;
                    return {
                        value: v,
                        itemStyle: {
                            color: isCurrent ? COLORS.danger : COLORS.primary,
                            borderColor: isCurrent ? '#000' : 'transparent',
                            borderWidth: isCurrent ? 2 : 0
                        }
                    };
                }),
                barMaxWidth: 22,
                label: { show: true, position: 'right' }
            }]
        };
    }

    function makeDocTopOption(data) {
        // 降序 + yAxis 默认从下往上 = 顶部显示最大值。
        var sorted = (data || []).slice().sort(function (a, b) { return b.value - a.value; });
        // 截断超长文件名
        var names = sorted.map(function (d) {
            var n = d.name || '(空)';
            return n.length > 28 ? n.substring(0, 26) + '…' : n;
        });
        var values = sorted.map(function (d) { return d.value || 0; });

        return {
            tooltip: baseTooltipAxis(),
            grid: { top: 10, left: 180, right: 30, bottom: 30 },
            xAxis: { type: 'value', minInterval: 1 },
            yAxis: { type: 'category', data: names, axisLabel: { interval: 0, fontSize: 11 } },
            series: [{
                name: '文档',
                type: 'bar',
                data: values.map(function (v) {
                    return { value: v, itemStyle: { color: COLORS.purple } };
                }),
                barMaxWidth: 22,
                label: { show: true, position: 'right' }
            }]
        };
    }

    function makeAnswerOption(data) {
        // Answered -> 已答（绿），Pending -> 未答（橙）
        return {
            tooltip: baseTooltip(),
            legend: { bottom: 0 },
            series: [{
                name: '回答状态',
                type: 'pie',
                radius: ['40%', '70%'],
                itemStyle: { borderRadius: 4, borderColor: '#fff', borderWidth: 2 },
                label: { formatter: '{b}\n{d}%' },
                data: (data || []).map(function (d) {
                    var color = (d.name === 'Answered' || d.name === '已答') ? COLORS.success : COLORS.orange;
                    return { name: d.name || '(空)', value: d.value || 0, itemStyle: { color: color } };
                })
            }]
        };
    }

    // ============================================================
    // 数据加载 + 渲染
    // ============================================================

    function loadQAType(signal) {
        return fetchJson(withTopic(urlQAType(), state.currentTopic), signal)
            .then(function (data) {
                var c = initChart('chart-qa-type', makeQATypeOption(data));
                hideLoading('chart-qa-type');
                return c;
            })
            .catch(function (e) {
                if (e && e.name === 'AbortError') return;
                console.error('[stats] QAType load failed:', e);
                hideLoading('chart-qa-type');
                showError('chart-qa-type');
            });
    }

    function loadQuality(signal) {
        return fetchJson(withTopic(urlQuality(), state.currentTopic), signal)
            .then(function (data) {
                var c = initChart('chart-quality', makeQualityOption(data));
                hideLoading('chart-quality');
                return c;
            })
            .catch(function (e) {
                if (e && e.name === 'AbortError') return;
                console.error('[stats] Quality load failed:', e);
                hideLoading('chart-quality');
                showError('chart-quality');
            });
    }

    function loadTopic(signal) {
        return fetchJson(urlTopic(), signal)
            .then(function (data) {
                var chart = initChart('chart-topic', makeTopicOption(data));
                hideLoading('chart-topic');
                if (chart) {
                    // 解除旧 handler：setOption(true) 已重置整个图，但 ECharts 5 不会自动清 events。
                    // 通过 off + on 模式确保只有最新一次 handler 在跑。
                    chart.off('click');
                    chart.on('click', function (params) {
                        if (!params || !params.name) return;
                        if (state.currentTopic === params.name) {
                            // 再次点击同一主题 -> 取消筛选
                            state.currentTopic = null;
                        } else {
                            state.currentTopic = params.name;
                        }
                        refreshAll(true /* skipTopic */);
                    });
                }
            })
            .catch(function (e) {
                if (e && e.name === 'AbortError') return;
                console.error('[stats] Topic load failed:', e);
                hideLoading('chart-topic');
                showError('chart-topic');
            });
    }

    function loadDocTop(signal) {
        return fetchJson(withTopic(urlDocTop(), state.currentTopic), signal)
            .then(function (data) {
                var c = initChart('chart-doc-top', makeDocTopOption(data));
                hideLoading('chart-doc-top');
                return c;
            })
            .catch(function (e) {
                if (e && e.name === 'AbortError') return;
                console.error('[stats] DocTop load failed:', e);
                hideLoading('chart-doc-top');
                showError('chart-doc-top');
            });
    }

    function loadAnswer(signal) {
        return fetchJson(withTopic(urlAnswer(), state.currentTopic), signal)
            .then(function (data) {
                var c = initChart('chart-answer', makeAnswerOption(data));
                hideLoading('chart-answer');
                return c;
            })
            .catch(function (e) {
                if (e && e.name === 'AbortError') return;
                console.error('[stats] Answer load failed:', e);
                hideLoading('chart-answer');
                showError('chart-answer');
            });
    }

    // 联动目标图 id（被 Topic 点击/清除筛选触发的「需要重拉」图）。
    var LINKED_CHART_IDS = ['chart-qa-type', 'chart-quality', 'chart-doc-top', 'chart-answer'];
    var ALL_CHART_IDS = LINKED_CHART_IDS.concat(['chart-topic']);

    function refreshAll(skipTopic) {
        updateClearButton();

        // 取消上一次未完成的请求，避免旧响应覆盖新数据导致图表与 state.currentTopic 错位。
        if (state.currentController) {
            state.currentController.abort();
        }
        var controller = new AbortController();
        state.currentController = controller;

        // 决定本次需要 loading 蒙层的图 id 集合。
        // skipTopic=true：点击主题，只对 4 张被联动图加载蒙层（主题图自身不动）。
        // skipTopic=false：清除筛选 / 首次加载，5 张全加。
        var ids = skipTopic ? LINKED_CHART_IDS : ALL_CHART_IDS;
        ids.forEach(showLoading);

        // 修复：ECharts 的 showLoading 会清掉容器 innerHTML 并渲染蒙层节点，
        // 而 initChart -> setOption(true) 会在蒙层之上重绘 canvas。
        // 蒙层是浮在 canvas 之上的 div，setOption 不会移除它，所以视觉上 loader 会持续显示。
        // 我们在 loadXxx 的 .then 末尾主动 hideLoading() 即可。

        var tasks = [
            loadQAType(controller.signal),
            loadQuality(controller.signal),
            loadDocTop(controller.signal),
            loadAnswer(controller.signal)
        ];
        if (!skipTopic) tasks.push(loadTopic(controller.signal));
        return Promise.all(tasks);
    }

    // ============================================================
    // 生命周期
    // ============================================================

    function bindClearButton() {
        var btn = $('filter-clear');
        if (!btn) return;
        btn.addEventListener('click', function () {
            state.currentTopic = null;
            refreshAll(false);
        });
    }

    function bindResize() {
        window.addEventListener('resize', function () {
            if (state.resizeTimer) clearTimeout(state.resizeTimer);
            state.resizeTimer = setTimeout(function () {
                Object.keys(state.charts).forEach(function (k) {
                    var c = state.charts[k];
                    if (c && !c.isDisposed()) c.resize();
                });
            }, 150);
        });
    }

    function bindUnload() {
        window.addEventListener('beforeunload', function () {
            Object.keys(state.charts).forEach(function (k) {
                var c = state.charts[k];
                if (c && !c.isDisposed()) c.dispose();
            });
            state.charts = {};
        });
    }

    // ============================================================
    // Bootstrap
    // ============================================================

    function init() {
        // 找 stats-panel-{projectId} 折叠容器（不再用固定 id，支持多实例共存）
        var panel = document.querySelector('[id^="stats-panel-"]');
        if (!panel) return;
        state.projectId = parseInt(panel.getAttribute('data-project-id'), 10) || 0;
        if (state.projectId <= 0) {
            console.warn('[stats] invalid projectId, abort.');
            return;
        }
        if (typeof echarts === 'undefined') {
            console.error('[stats] ECharts not loaded.');
            ['chart-qa-type', 'chart-quality', 'chart-topic', 'chart-doc-top', 'chart-answer']
                .forEach(function (id) { showError(id, 'ECharts 未加载'); });
            return;
        }

        bindClearButton();
        bindResize();
        bindUnload();
        bindCollapse(panel);

        // 关键变更：折叠状态下 ECharts 容器 display:none，尺寸为 0，初始化会得到 0×0 画布。
        // 因此不在 DOMContentLoaded 时 init 图表，而是监听 panel 的 show.bs.collapse 事件：
        //   首次展开 → 拉取并 init 5 个图
        //   后续展开 → 对已 init 的图调用 resize() 重排
        //   折叠时 → 不主动 dispose，让浏览器 hide，下次展开再 resize（保留 currentTopic 状态）
        if (panel.classList.contains('show')) {
            // 页面加载时折叠容器已展开（罕见，防御）→ 立即 init
            refreshAll(false);
            state.initialized = true;
        }
    }

    function bindCollapse(panel) {
        panel.addEventListener('show.bs.collapse', function () {
            // 等 Bootstrap transition 完成（默认 350ms）再 init/resize
            setTimeout(function () {
                if (state.initialized) {
                    Object.keys(state.charts).forEach(function (k) {
                        var c = state.charts[k];
                        if (c && !c.isDisposed()) c.resize();
                    });
                } else {
                    refreshAll(false);
                    state.initialized = true;
                }
            }, 350);
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
