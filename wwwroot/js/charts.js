// ── Chart.js — Vehicle Management Dashboard ──
// Data-Dense Dashboard 风格，简洁现代专业设计
// 配色：Cyan 主题 #0891B2

// 全局主题配置
var chartConfig = {
    colors: {
        // Cyan 主题色系
        primary: '#0891B2',        // 主色
        primaryLight: '#22D3EE',   // 亮色
        primaryBg: 'rgba(8, 145, 178, 0.08)',
        
        // 数据色系（按重要性排序）
        data1: '#0891B2',  // Cyan
        data2: '#0E7490',  // Deep Cyan
        data3: '#22D3EE',  // Light Cyan
        data4: '#F59E0B',  // Amber
        data5: '#10B981',  // Emerald
        data6: '#6366F1',  // Indigo
        
        // UI 元素
        text: '#164E63',           // 深色文字
        textMuted: '#64748B',      // 浅色文字
        grid: 'rgba(203, 213, 225, 0.5)',
        gridLight: 'rgba(226, 232, 240, 0.4)',
        tooltip: 'rgba(15, 23, 42, 0.95)',
        white: '#FFFFFF'
    },
    fonts: {
        family: "'Geist', -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif"
    }
};

// Chart.js 全局默认配置
Chart.defaults.font.family = chartConfig.fonts.family;
Chart.defaults.font.size = 12;
Chart.defaults.color = chartConfig.colors.textMuted;
Chart.defaults.plugins.legend.labels.usePointStyle = true;
Chart.defaults.plugins.legend.labels.padding = 16;
Chart.defaults.plugins.tooltip.backgroundColor = chartConfig.colors.tooltip;
Chart.defaults.plugins.tooltip.titleFont = { 
    family: chartConfig.fonts.family, 
    size: 13, 
    weight: '600' 
};
Chart.defaults.plugins.tooltip.bodyFont = { 
    family: chartConfig.fonts.family, 
    size: 12,
    weight: '500'
};
Chart.defaults.plugins.tooltip.padding = 10;
Chart.defaults.plugins.tooltip.cornerRadius = 6;
Chart.defaults.plugins.tooltip.displayColors = true;
Chart.defaults.plugins.tooltip.boxPadding = 4;

// 设置 Chart.js 使用设备像素比，提高清晰度
Chart.defaults.devicePixelRatio = 2;

// 导出图表管理对象
window.charts = {
    _instances: {},

    destroyChart: function (canvasId) {
        if (this._instances[canvasId]) {
            this._instances[canvasId].destroy();
            delete this._instances[canvasId];
        }
    },

    // 环形图 - 用于分布统计
    createPieChart: function (canvasId, labels, data, title) {
        this.destroyChart(canvasId);
        var ctx = document.getElementById(canvasId);
        if (!ctx) return;
        
        // 使用 Cyan 渐变色系
        var colors = [
            chartConfig.colors.data1,
            chartConfig.colors.data2,
            chartConfig.colors.data3,
            chartConfig.colors.data4,
            chartConfig.colors.data5,
            chartConfig.colors.data6
        ].slice(0, data.length);
        
        this._instances[canvasId] = new Chart(ctx, {
            type: 'doughnut',
            data: {
                labels: labels,
                datasets: [{
                    data: data,
                    backgroundColor: colors,
                    borderColor: '#fff',
                    borderWidth: 2,
                    hoverOffset: 6,
                    spacing: 2
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: true,
                cutout: '70%',
                plugins: {
                    title: { 
                        display: !!title, 
                        text: title, 
                        font: { 
                            size: 15, 
                            weight: '600', 
                            family: chartConfig.fonts.family 
                        }, 
                        color: chartConfig.colors.text,
                        align: 'start',
                        padding: { bottom: 16 }
                    },
                    legend: { 
                        position: 'bottom',
                        labels: {
                            padding: 12,
                            usePointStyle: true,
                            pointStyle: 'circle',
                            font: { 
                                size: 11, 
                                weight: '500', 
                                family: chartConfig.fonts.family 
                            },
                            color: chartConfig.colors.textMuted
                        }
                    },
                    tooltip: {
                        backgroundColor: chartConfig.colors.tooltip,
                        titleFont: { 
                            family: chartConfig.fonts.family, 
                            size: 13, 
                            weight: '600' 
                        },
                        bodyFont: { 
                            family: chartConfig.fonts.family, 
                            size: 12,
                            weight: '500'
                        },
                        padding: 12,
                        cornerRadius: 6,
                        displayColors: true,
                        boxPadding: 6,
                        callbacks: {
                            label: function (context) {
                                var total = context.dataset.data.reduce(function (a, b) { return a + b; }, 0);
                                var pct = ((context.parsed / total) * 100).toFixed(1);
                                return context.label + ': ' + context.parsed + ' (' + pct + '%)';
                            }
                        }
                    }
                },
                animation: {
                    animateRotate: true,
                    duration: 600,
                    easing: 'easeOutQuart'
                }
            }
        });
    },

    // 柱状图 - 用于部门车辆分布
    createBarChart: function (canvasId, labels, data, title, horizontal) {
        this.destroyChart(canvasId);
        var ctx = document.getElementById(canvasId);
        if (!ctx) return;
        
        var self = this;
        var isHorizontal = horizontal === true;
        
        // 延迟初始化，确保 DOM 就绪
        requestAnimationFrame(function() {
            requestAnimationFrame(function() {
                setTimeout(function() {
                    // 按数据量使用渐变色系
                    var barColors = data.map(function (_, i) { 
                        return [
                            chartConfig.colors.data1,
                            chartConfig.colors.data2,
                            chartConfig.colors.data3,
                            chartConfig.colors.data4,
                            chartConfig.colors.data5,
                            chartConfig.colors.data6
                        ][i % 6]; 
                    });
                    
                    // 智能判断标签是否需要旋转
                    var maxLabelLength = Math.max.apply(null, labels.map(function(l) { return l.length; }));
                    var needsRotation = maxLabelLength > 6;
                    var labelRotation = needsRotation ? -30 : 0;
                    
                    self._instances[canvasId] = new Chart(ctx, {
                        type: 'bar',
                        data: {
                            labels: labels,
                            datasets: [{
                                data: data,
                                backgroundColor: barColors,
                                borderColor: 'transparent',
                                borderWidth: 0,
                                borderRadius: 4,
                                borderSkipped: false,
                                categoryPercentage: 0.7,
                                barPercentage: 0.65
                            }]
                        },
                        options: {
                            indexAxis: isHorizontal ? 'y' : 'x',
                            responsive: true,
                            maintainAspectRatio: false,
                            layout: {
                                padding: {
                                    top: 4,
                                    right: 8,
                                    bottom: needsRotation ? 45 : 10,
                                    left: 4
                                }
                            },
                            plugins: {
                                title: { 
                                    display: !!title, 
                                    text: title, 
                                    font: { 
                                        size: 15, 
                                        weight: '600', 
                                        family: chartConfig.fonts.family 
                                    }, 
                                    color: chartConfig.colors.text,
                                    align: 'start',
                                    padding: { bottom: 12 }
                                },
                                legend: { display: false },
                                tooltip: {
                                    backgroundColor: chartConfig.colors.tooltip,
                                    titleFont: { 
                                        family: chartConfig.fonts.family, 
                                        size: 13, 
                                        weight: '600' 
                                    },
                                    bodyFont: { 
                                        family: chartConfig.fonts.family, 
                                        size: 12,
                                        weight: '500'
                                    },
                                    padding: 12,
                                    cornerRadius: 6,
                                    displayColors: true,
                                    boxPadding: 6,
                                    callbacks: {
                                        label: function(context) {
                                            return '数量: ' + context.parsed.y + ' 辆';
                                        }
                                    }
                                }
                            },
                            scales: {
                                x: {
                                    beginAtZero: true,
                                    ticks: {
                                        precision: 0,
                                        color: '#8E8E8E',
                                        font: { 
                                            family: "'Geist', -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif", 
                                            size: 13, 
                                            weight: '500',
                                            lineHeight: 1.5
                                        },
                                        maxRotation: labelRotation,
                                        minRotation: labelRotation,
                                        autoSkip: false,
                                        padding: 6
                                    },
                                    grid: {
                                        color: chartConfig.colors.gridLight,
                                        drawBorder: false,
                                        lineWidth: 1,
                                        drawTicks: false
                                    },
                                    border: { display: false }
                                },
                                y: {
                                    beginAtZero: true,
                                    ticks: {
                                        precision: 0,
                                        color: '#8E8E8E',
                                        font: { 
                                            family: "'Geist', -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif", 
                                            size: 13, 
                                            weight: '500',
                                            lineHeight: 1.5
                                        },
                                        padding: 8,
                                        stepSize: 1,
                                        backdropColor: 'transparent'
                                    },
                                    grid: {
                                        color: chartConfig.colors.gridLight,
                                        drawBorder: false,
                                        lineWidth: 1,
                                        drawTicks: false
                                    },
                                    border: { display: false }
                                }
                            },
                            interaction: {
                                intersect: false,
                                mode: 'nearest'
                            },
                            animation: {
                                duration: 700,
                                easing: 'easeOutCubic',
                                delay: function(context) {
                                    return context.dataIndex * 100;
                                }
                            }
                        }
                    });
                    
                    // 强制 resize 确保尺寸正确
                    setTimeout(function() {
                        if (self._instances[canvasId]) {
                            self._instances[canvasId].resize();
                        }
                    }, 100);
                }, 0);
            });
        });
    },

    // 折线图 - 用于趋势分析
    createLineChart: function (canvasId, labels, data, title) {
        this.destroyChart(canvasId);
        var ctx = document.getElementById(canvasId);
        if (!ctx) return;
        
        var self = this;
        
        requestAnimationFrame(function() {
            requestAnimationFrame(function() {
                setTimeout(function() {
                    self._instances[canvasId] = new Chart(ctx, {
                        type: 'line',
                        data: {
                            labels: labels,
                            datasets: [{
                                label: '使用次数',
                                data: data,
                                borderColor: chartConfig.colors.primary,
                                backgroundColor: function(context) {
                                    var chart = context.chart;
                                    var {ctx: chartCtx, chartArea} = chart;
                                    if (!chartArea) return chartConfig.colors.primaryBg;
                                    
                                    var gradient = chartCtx.createLinearGradient(
                                        0, chartArea.top, 
                                        0, chartArea.bottom
                                    );
                                    gradient.addColorStop(0, 'rgba(8, 145, 178, 0.2)');
                                    gradient.addColorStop(0.8, 'rgba(8, 145, 178, 0.05)');
                                    gradient.addColorStop(1, 'rgba(8, 145, 178, 0)');
                                    return gradient;
                                },
                                pointBackgroundColor: '#fff',
                                pointBorderColor: chartConfig.colors.primary,
                                pointBorderWidth: 2,
                                pointRadius: 3,
                                pointHoverRadius: 6,
                                pointHoverBorderWidth: 3,
                                pointHoverBackgroundColor: chartConfig.colors.primary,
                                pointHoverBorderColor: '#fff',
                                borderWidth: 2.5,
                                fill: true,
                                tension: 0.4,
                                hoverBorderWidth: 2.5
                            }]
                        },
                        options: {
                            responsive: true,
                            maintainAspectRatio: false,
                            layout: {
                                padding: {
                                    top: 4,
                                    right: 8,
                                    bottom: 8,
                                    left: 4
                                }
                            },
                            plugins: {
                                title: { 
                                    display: !!title, 
                                    text: title, 
                                    font: { 
                                        size: 15, 
                                        weight: '600', 
                                        family: chartConfig.fonts.family 
                                    }, 
                                    color: chartConfig.colors.text,
                                    align: 'start',
                                    padding: { bottom: 12 }
                                },
                                legend: { display: false },
                                tooltip: {
                                    backgroundColor: chartConfig.colors.tooltip,
                                    titleFont: { 
                                        family: chartConfig.fonts.family, 
                                        size: 13, 
                                        weight: '600' 
                                    },
                                    bodyFont: { 
                                        family: chartConfig.fonts.family, 
                                        size: 12,
                                        weight: '500'
                                    },
                                    padding: 12,
                                    cornerRadius: 6,
                                    displayColors: true,
                                    boxPadding: 6,
                                    callbacks: {
                                        label: function(context) {
                                            return '使用次数: ' + context.parsed.y + ' 次';
                                        }
                                    }
                                }
                            },
                            scales: {
                                y: {
                                    beginAtZero: true,
                                    ticks: {
                                        precision: 0,
                                        color: '#8E8E8E',
                                        font: { 
                                            family: "'Geist', -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif", 
                                            size: 13, 
                                            weight: '500',
                                            lineHeight: 1.5
                                        },
                                        padding: 8,
                                        stepSize: 2,
                                        backdropColor: 'transparent'
                                    },
                                    grid: {
                                        color: chartConfig.colors.gridLight,
                                        drawBorder: false,
                                        lineWidth: 1,
                                        drawTicks: false
                                    },
                                    border: { display: false }
                                },
                                x: {
                                    ticks: {
                                        color: '#8E8E8E',
                                        font: { 
                                            family: "'Geist', -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif", 
                                            size: 13, 
                                            weight: '500',
                                            lineHeight: 1.5
                                        },
                                        padding: 6,
                                        maxRotation: 0,
                                        autoSkip: false,
                                        backdropColor: 'transparent'
                                    },
                                    grid: {
                                        display: false,
                                        drawTicks: false
                                    },
                                    border: { display: false }
                                }
                            },
                            interaction: {
                                intersect: false,
                                mode: 'index'
                            },
                            animation: {
                                duration: 800,
                                easing: 'easeOutQuart',
                                delay: function(context) {
                                    return context.dataIndex * 120;
                                }
                            }
                        }
                    });
                    
                    // 强制 resize 确保尺寸正确
                    setTimeout(function() {
                        if (self._instances[canvasId]) {
                            self._instances[canvasId].resize();
                        }
                    }, 100);
                }, 0);
            });
        });
    },

    // 更新柱状图数据
    updateBarChart: function (canvasId, labels, data) {
        var chart = this._instances[canvasId];
        if (chart) {
            chart.data.labels = labels;
            chart.data.datasets[0].data = data;
            chart.update('active');
        } else {
            this.createBarChart(canvasId, labels, data, '', false);
        }
    }
};
