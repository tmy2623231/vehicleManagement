using System;
using System.Text.RegularExpressions;
using vehicleManagement.aiServer.BLL.Enums;

#nullable enable

namespace vehicleManagement.aiServer.BLL
{
    /// <summary>
    /// 用户意图识别器：基于关键词匹配判断用户查询意图
    /// </summary>
    public static class IntentDetector
    {
        /// <summary>
        /// 根据用户输入识别意图类型，并提取搜索关键词
        /// </summary>
        public static (UserIntent Intent, string? Keyword) Detect(string content)
        {
            var text = content.ToLower();

            // 车辆统计（汇总/占比/分布/比较级）- 优先于具体查询以确保统计意图被正确识别
            string[] statsKeywords = { "统计", "汇总", "多少辆", "几辆", "几台", "多少", "几个", "占比", "分布", "趋势", "排行", "最多", "最少", "哪个部门" };
            foreach (var kw in statsKeywords)
            {
                if (text.Contains(kw))
                    return (UserIntent.VehicleStats, null);
            }

            // 使用记录
            string[] recordKeywords = { "使用记录", "出车", "用车", "行程", "出车记录", "用车记录" };
            foreach (var kw in recordKeywords)
            {
                if (text.Contains(kw))
                {
                    var keyword = ExtractKeyword(content, recordKeywords);
                    return (UserIntent.UsageRecordQuery, keyword);
                }
            }

            // ★ 在用车辆查询（优先级高于普通车辆查询，避免"在用"关键词冲突）
            string[] activeVehicleKeywords = { "在用车辆", "当前可用", "正在使用", "有哪些车", "所有车辆", "全部车辆", "车辆列表" };
            foreach (var kw in activeVehicleKeywords)
            {
                if (text.Contains(kw))
                {
                    // 排除车牌号模式：如果输入包含车牌号，不走 ActiveVehicleQuery
                    if (Regex.IsMatch(content, @"[京津沪渝冀豫云辽黑湘皖鲁新苏浙赣鄂桂甘晋蒙陕吉闽贵粤川青藏琼宁][A-Za-z]\d{4,5}"))
                    {
                        var match = Regex.Match(content, @"[京津沪渝冀豫云辽黑湘皖鲁新苏浙赣鄂桂甘晋蒙陕吉闽贵粤川青藏琼宁][A-Za-z]\d{4,5}");
                        return (UserIntent.VehicleQuery, match.Value);
                    }
                    // 有附带关键词（品牌/型号等）时走 VehicleQuery 进行过滤
                    var remnant = ExtractKeyword(content, activeVehicleKeywords);
                    if (!string.IsNullOrEmpty(remnant) && IsMeaningfulKeyword(remnant))
                        return (UserIntent.VehicleQuery, remnant);
                    return (UserIntent.ActiveVehicleQuery, null);
                }
            }

            // 车辆查询
            string[] vehicleKeywords = { "车辆", "车牌", "品牌", "型号", "状态", "维修", "退役", "公里", "颜色" };
            foreach (var kw in vehicleKeywords)
            {
                if (text.Contains(kw))
                {
                    var keyword = ExtractKeyword(content, vehicleKeywords);
                    return (UserIntent.VehicleQuery, keyword);
                }
            }

            // 部门查询
            string[] deptKeywords = { "部门", "单位", "科室" };
            foreach (var kw in deptKeywords)
            {
                if (text.Contains(kw))
                {
                    var keyword = ExtractKeyword(content, deptKeywords);
                    return (UserIntent.DepartmentQuery, keyword);
                }
            }

            // 人员查询
            string[] personnelKeywords = { "人员", "员工", "司机", "姓名", "电话", "手机" };
            foreach (var kw in personnelKeywords)
            {
                if (text.Contains(kw))
                {
                    var keyword = ExtractKeyword(content, personnelKeywords);
                    return (UserIntent.PersonnelQuery, keyword);
                }
            }

            // 检测是否有车牌号模式（如"京A12345"）
            if (Regex.IsMatch(content, @"[京津沪渝冀豫云辽黑湘皖鲁新苏浙赣鄂桂甘晋蒙陕吉闽贵粤川青藏琼宁][A-Za-z]\d{4,5}"))
            {
                var match = Regex.Match(content, @"[京津沪渝冀豫云辽黑湘皖鲁新苏浙赣鄂桂甘晋蒙陕吉闽贵粤川青藏琼宁][A-Za-z]\d{4,5}");
                return (UserIntent.VehicleQuery, match.Value);
            }

            // 纯闲聊/帮助关键词（仅在没有命中任何数据查询意图时才生效）
            // 排除包含数据查询关键词的组合输入（如"怎么查京A"不应该被拦截）
            string[] generalKeywords = { "你好", "谢谢", "再见", "帮助", "功能", "版本" };
            foreach (var kw in generalKeywords)
            {
                if (text.Contains(kw))
                    return (UserIntent.GeneralChat, null);
            }

            // 默认：通用问答
            return (UserIntent.GeneralChat, null);
        }

        /// <summary>
        /// 判断残留关键词是否为有效搜索词（排除纯噪音片段）
        /// </summary>
        private static bool IsMeaningfulKeyword(string keyword)
        {
            if (keyword.Length < 2) return false;
            // 常见噪声字符：状态词、量词、疑问词等
            string[] noiseChars = { "辆", "在", "用", "有", "哪", "几", "个", "台",
                "什", "么", "些", "谁", "是", "吗", "呢", "嘛", "吧", "呀", "哦", "啊",
                "的", "了", "和", "或", "与" };
            var meaningful = keyword;
            foreach (var c in noiseChars)
                meaningful = meaningful.Replace(c, "");
            return meaningful.Trim().Length >= 1;
        }

        /// <summary>
        /// 从用户输入中提取搜索关键词（去除意图触发词后的剩余文本）
        /// </summary>
        private static string? ExtractKeyword(string content, string[] triggerWords)
        {
            var result = content;
            foreach (var word in triggerWords)
            {
                result = result.Replace(word, "", StringComparison.OrdinalIgnoreCase);
            }
            // 去除常见停用词
            string[] stopWords = { "查询", "查查", "查一查", "查", "看看", "帮我", "请",
                "一下", "搜索", "所有的", "所有", "全部", "列表", "最近", "的",
                "目前", "当前", "现在", "为止", "当下", "截至", "中有", "有" };
            foreach (var sw in stopWords)
            {
                result = result.Replace(sw, "", StringComparison.OrdinalIgnoreCase);
            }
            var trimmed = result.Trim(' ', '，', ',', '。', '.', '！', '?', '？', '\t', '\n', '\r');
            return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
        }
    }
}
