/**
 * 布局填充检测工具 v2 — 全覆盖检测侧边栏+主区域+像素级空白
 *
 * 调用方式:
 *   window.checkLayoutFill()          → 返回检测 JSON
 *   window.checkLayoutFill({draw:true}) → 返回 JSON + 在页面上画出红色检测框
 *   window.checkLayoutFill({clear:true}) → 清除红色检测框
 */

window.checkLayoutFill = function (opts) {
    opts = opts || {};
    var draw = !!opts.draw;
    if (!!opts.clear) { clearMarkers(); return { cleared: true }; }
    if (draw) clearMarkers();

    var results = { ok: true, gaps: [], emptyPct: 0, totalW: 0, totalH: 0, details: {} };
    var GAP_THRESHOLD = 3; // 间隙超过 3px 才报告

    // ─── 辅助函数：记录空白区域 ───
    function addGap(label, x, y, w, h, containerH, drawColor) {
        if (h < GAP_THRESHOLD && w < GAP_THRESHOLD) return;
        results.ok = false;
        var pctVal = containerH > 0 ? (h / containerH * 100) : 0;
        var gap = {
            selector: label,
            x: Math.round(x), y: Math.round(y),
            w: Math.round(w), h: Math.round(h),
            pct: pctVal.toFixed(1) + '%',
            pctNum: pctVal
        };
        results.gaps.push(gap);
        if (draw) drawMarker(label, x, y, w, h, drawColor || '#ef4444');
    }

    // ─── 辅助函数：画检测红框 ───
    function drawMarker(label, x, y, w, h, color) {
        var el = document.createElement('div');
        el.className = '__aim-marker';
        el.style.cssText = [
            'position:fixed;z-index:9998;pointer-events:none',
            'left:' + x + 'px;top:' + y + 'px',
            'width:' + w + 'px;height:' + h + 'px',
            'border:2px dashed ' + color,
            'background:' + color + '15'
        ].join(';');
        var tip = document.createElement('div');
        tip.textContent = label + ' (' + Math.round(w) + '×' + Math.round(h) + ')';
        tip.style.cssText = [
            'position:fixed;z-index:9999;pointer-events:none',
            'left:' + x + 'px;top:' + Math.max(0, y - 20) + 'px',
            'background:' + color + ';color:#fff;font-size:10px',
            'padding:1px 6px;border-radius:3px;font-family:monospace',
            'white-space:nowrap'
        ].join(';');
        document.body.appendChild(el);
        document.body.appendChild(tip);
    }

    // ─── 辅助函数：获取元素矩形 ───
    function rect(el) { return el ? el.getBoundingClientRect() : null; }

    // ================================================================
    //  1. 整体根容器：.aim-root 是否填满 .main-content
    // ================================================================
    var rootEl = document.querySelector('.aim-root');
    var mcEl = document.querySelector('.main-content');
    if (!rootEl || !mcEl) return { ok: false, error: '缺少 .aim-root 或 .main-content' };

    var rr = rect(rootEl);
    var mr = rect(mcEl);
    results.totalW = rr.width;
    results.totalH = rr.height;
    results.details.mainContent = fmt(mr);
    results.details.aimRoot = fmt(rr);

    var ovf = { top: rr.top - mr.top, right: mr.right - rr.right, bottom: mr.bottom - rr.bottom, left: rr.left - mr.left };
    results.details.overflow = ovf;
    if (ovf.bottom > 2) addGap('.aim-root 底部未填充 .main-content', mr.left, rr.bottom, mr.width, ovf.bottom, mr.height, '#ef4444');
    if (ovf.top > 2)    addGap('.aim-root 顶部未填充 .main-content', mr.left, mr.top, mr.width, ovf.top, mr.height, '#f97316');

    // ================================================================
    //  2. 主区域 .aim-main 内部填充检测
    // ================================================================
    var mainEl = document.querySelector('.aim-main');
    if (mainEl) {
        var mn = rect(mainEl);
        var children = mainEl.children;
        var lastBottom = mn.top;

        for (var i = 0; i < children.length; i++) {
            var child = children[i];
            var cr = rect(child);
            var label = (child.className || child.tagName).replace(/\s+/g, '.');
            results.details['main-child-' + i] = fmt(cr, label);

            // 相邻子元素间隙
            if (i > 0 && cr.top - rect(children[i - 1]).bottom > GAP_THRESHOLD) {
                addGap('main-间隙[' + i + ']', cr.left, rect(children[i - 1]).bottom,
                    cr.width, cr.top - rect(children[i - 1]).bottom, mn.height, '#f59e0b');
            }
            if (cr.bottom > lastBottom) lastBottom = cr.bottom;
        }

        var mainBottomGap = mn.bottom - lastBottom;
        if (mainBottomGap > 4) {
            addGap('.aim-main 底部空白', mn.left, lastBottom, mn.width, mainBottomGap, mn.height, '#ef4444');
        }
        results.details.aimMain = fmt(mn);
        results.details.mainLastBottom = Math.round(lastBottom);
        results.details.mainBottomGap = Math.round(mainBottomGap);
    }

    // ================================================================
    //  3. ★ 侧边栏 .aim-side 内部填充检测（图片红色框选区域）
    // ================================================================
    var sideEl = document.querySelector('.aim-side');
    if (sideEl) {
        var sn = rect(sideEl);
        var sideChildren = sideEl.children;
        var sideLastBottom = sn.top;

        results.details.aimSide = fmt(sn);

        var sideHeadEl = document.querySelector('.aim-side__head');
        var sideListEl = document.querySelector('.aim-side__list');
        var sideMemEl = document.querySelector('.aim-mem');

        // 3a. 侧边栏子元素间间隙（head → list → mem）
        if (sideHeadEl && sideListEl) {
            var hr = rect(sideHeadEl);
            var lr = rect(sideListEl);
            var headToListGap = lr.top - hr.bottom;
            results.details.sideHeadToListGap = Math.round(headToListGap);
            if (headToListGap > GAP_THRESHOLD) {
                addGap('侧边栏: 头部→列表 间隙',
                    lr.left, hr.bottom, lr.width, headToListGap, sn.height, '#6366f1');
            }
        }
        if (sideListEl && sideMemEl) {
            var lr2 = rect(sideListEl);
            var mr2 = rect(sideMemEl);
            var listToMemGap = mr2.top - lr2.bottom;
            results.details.sideListToMemGap = Math.round(listToMemGap);
            if (listToMemGap > GAP_THRESHOLD) {
                addGap('侧边栏: 列表→记忆 间隙',
                    mr2.left, lr2.bottom, mr2.width, listToMemGap, sn.height, '#8b5cf6');
            }
        }

        // 3b. 侧边栏底部空白（最后一个子元素底部 vs aim-side 底部）
        for (var k = 0; k < sideChildren.length; k++) {
            var sc = rect(sideChildren[k]);
            var sl = (sideChildren[k].className || sideChildren[k].tagName).replace(/\s+/g, '.');
            results.details['side-child-' + k] = fmt(sc, sl);
            if (sc.bottom > sideLastBottom) sideLastBottom = sc.bottom;
        }

        var sideBottomGap = sn.bottom - sideLastBottom;
        results.details.sideBottomGap = Math.round(sideBottomGap);
        if (sideBottomGap > 4) {
            addGap('★ 侧边栏底部空白', sn.left, sideLastBottom,
                sn.width, sideBottomGap, sn.height, '#dc2626');
        }

        // 3c. ── 侧边栏列表区内部空白 ──
        //     .aim-side__list 是 flex:1 + overflow:auto
        //     当会话少时，内容顶部有大量滚动空白
        if (sideListEl) {
            var slr = rect(sideListEl);
            var listItems = sideListEl.querySelectorAll('.aim-sess');
            if (listItems.length === 0) {
                // 完全空列表 → 检查 .aim-empty 是否居中/铺满
                var emptyEl = sideListEl.querySelector('.aim-empty');
                if (emptyEl) {
                    var er = rect(emptyEl);
                    var listTopGap = er.top - slr.top;
                    var listBotGap = slr.bottom - er.bottom;
                    results.details.sideListEmptyGap = { top: Math.round(listTopGap), bottom: Math.round(listBotGap) };
                    if (listTopGap > 10 || listBotGap > 10) {
                        var centerOffset = Math.abs(listTopGap - listBotGap);
                        addGap('侧边栏列表: 空状态偏移 ' + Math.round(centerOffset) + 'px',
                            slr.left, slr.top, slr.width, slr.height, sn.height, '#dc2626');
                    }
                } else {
                    // 整个列表区空白无内容
                    addGap('★ 侧边栏列表区: 完全空白', slr.left, slr.top,
                        slr.width, slr.height, sn.height, '#dc2626');
                }
            } else {
                // 有会话 → 检查首条到顶部、末条到底部的空白
                var firstItem = listItems[0];
                var lastItem = listItems[listItems.length - 1];
                var fir = rect(firstItem);
                var lar = rect(lastItem);
                var listHeadGap = fir.top - slr.top;
                var listTailGap = slr.bottom - lar.bottom;
                results.details.sideListHeadGap = Math.round(listHeadGap);
                results.details.sideListTailGap = Math.round(listTailGap);
                if (listHeadGap > 4) {
                    addGap('侧边栏列表: 顶部 padding', slr.left, slr.top,
                        slr.width, listHeadGap, sn.height, '#6366f1');
                }
                if (listTailGap > 10) {
                    addGap('★ 侧边栏列表: 底部大段空白',
                        slr.left, lar.bottom, slr.width, listTailGap, sn.height, '#dc2626');
                }
            }
        }

        // 3d. 记忆面板内部检测
        if (sideMemEl) {
            var memExpandList = sideMemEl.querySelector('.aim-mem__list');
            if (memExpandList) {
                var mlr = rect(memExpandList);
                var memTogEl = sideMemEl.querySelector('.aim-mem__tog');
                var mtr = rect(memTogEl);
                if (mtr) {
                    var memGap = mlr.top - mtr.bottom;
                    results.details.memToggleGap = Math.round(memGap);
                }
            }
        }
    }

    // ================================================================
    //  4. 欢迎页 .aim-hero 内容居中检测
    // ================================================================
    var heroEl = document.querySelector('.aim-hero');
    if (heroEl) {
        var her = rect(heroEl);
        var cardEl = heroEl.querySelector('.aim-hero__card');
        if (cardEl) {
            var crd = rect(cardEl);
            var heroTopGap = crd.top - her.top;
            var heroBotGap = her.bottom - crd.bottom;
            results.details.hero = fmt(her);
            results.details.heroCard = fmt(crd);
            results.details.heroGaps = { top: Math.round(heroTopGap), bottom: Math.round(heroBotGap) };
            if (heroBotGap > 20 || heroTopGap > 20) {
                addGap('.aim-hero 内容未居中 (上下差' + Math.round(Math.abs(heroTopGap - heroBotGap)) + 'px)',
                    her.left, crd.bottom, her.width, heroBotGap, her.height, '#ef4444');
            }
        }
    }

    // ================================================================
    //  5. 消息区 .aim-msgs 底部空白
    // ================================================================
    var msgsEl = document.querySelector('.aim-msgs');
    if (msgsEl) {
        var msr = rect(msgsEl);
        results.details.messages = fmt(msr);
        if (msgsEl.children.length > 0) {
            var lastMsg = msgsEl.lastElementChild;
            var lmr = rect(lastMsg);
            var msgsGap = msr.bottom - lmr.bottom;
            results.details.msgsBottomGap = Math.round(msgsGap);
            if (msgsGap > 10) {
                addGap('.aim-msgs 底部空白', lmr.left, lmr.bottom,
                    lmr.width, msgsGap, msr.height, '#f59e0b');
            }
        }
    }

    // ================================================================
    //  6. 汇总
    // ================================================================
    var totalPct = 0;
    for (var j = 0; j < results.gaps.length; j++) {
        totalPct += results.gaps[j].pctNum || 0;
    }
    results.emptyPct = parseFloat(Math.min(totalPct, 100).toFixed(1));
    if (results.emptyPct > 5) results.ok = false;

    // 判定结论
    results.verdict = results.ok
        ? '✅ 页面内容已铺满，未检测到明显空白区域'
        : '❌ 检测到 ' + results.gaps.length + ' 处空白，总面积占比 ' + results.emptyPct + '%';

    return results;
};

/** 清除所有标记框 */
function clearMarkers() {
    var markers = document.querySelectorAll('.__aim-marker');
    for (var m = 0; m < markers.length; m++) markers[m].remove();
}

/** 格式化矩形 -> 纯值对象 */
function fmt(rect, label) {
    var o = { w: Math.round(rect.width), h: Math.round(rect.height), x: Math.round(rect.left), y: Math.round(rect.top) };
    if (label) o.selector = label;
    return o;
}
