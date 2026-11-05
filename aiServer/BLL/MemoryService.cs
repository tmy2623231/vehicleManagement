using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using vehicleManagement.aiServer.DAL.Model;
using vehicleManagement.aiServer.DAL.Repository;

namespace vehicleManagement.aiServer.BLL
{
    /// <summary>
    /// 记忆服务实现：关键词提取 + 模糊搜索 + 上下文构建
    /// </summary>
    public class MemoryService
    {
        private readonly ChatMemoryRepository _memoryRepo;
        private readonly ILogger<MemoryService> _logger;

        // 中文关键词提取规则：车牌号、常见车辆品牌、部门/人员相关词
        private static readonly Regex LicensePlateRegex = new(
            @"([京津沪渝冀豫云辽黑湘皖鲁新苏浙赣鄂桂甘晋蒙陕吉闽贵粤川青藏琼宁][A-Z][A-HJ-NP-Z0-9]{4,5}[A-HJ-NP-Z0-9挂学警港澳])",
            RegexOptions.Compiled);

        private static readonly string[] VehicleBrands = {
            "丰田", "本田", "日产", "大众", "别克", "福特", "雪佛兰", "现代", "起亚",
            "奥迪", "宝马", "奔驰", "红旗", "长安", "吉利", "长城", "比亚迪", "奇瑞",
            "特斯拉", "蔚来", "小鹏", "理想", "五菱", "传祺", "荣威", "名爵"
        };

        private static readonly string[] PreferenceKeywords = {
            "喜欢", "常用", "习惯", "一般", "经常", "偏好", "倾向于",
            "我的", "我们", "我需要", "我想", "我要"
        };

        public MemoryService(ChatMemoryRepository memoryRepo, ILogger<MemoryService> logger)
        {
            _memoryRepo = memoryRepo;
            _logger = logger;
        }

        /// <summary>
        /// 从一轮对话中提取关键信息并存储为记忆
        /// </summary>
        public async Task ExtractAndStoreAsync(string userMessage, string assistantResponse, int sessionId)
        {
            try
            {
                // 1. 提取车牌号
                var plates = LicensePlateRegex.Matches(userMessage);
                foreach (Match match in plates)
                {
                    var memoryKey = $"vehicle_plate_{match.Value}";
                    await UpsertMemoryAsync(memoryKey, $"用户提及了车牌号为 {match.Value} 的车辆",
                        "vehicle", sessionId);
                }

                // 2. 提取车辆品牌
                foreach (var brand in VehicleBrands)
                {
                    if (userMessage.Contains(brand))
                    {
                        var memoryKey = $"vehicle_brand_{brand}";
                        await UpsertMemoryAsync(memoryKey, $"用户提及了 {brand} 品牌车辆",
                            "vehicle", sessionId);
                    }
                }

                // 3. 提取用户偏好/习惯
                foreach (var keyword in PreferenceKeywords)
                {
                    if (userMessage.Contains(keyword))
                    {
                        // 取关键词前后文作为记忆内容
                        var idx = userMessage.IndexOf(keyword);
                        var start = Math.Max(0, idx - 5);
                        var len = Math.Min(userMessage.Length - start, 100);
                        var snippet = userMessage.Substring(start, len);

                        var memoryKey = $"preference_{DateTime.Now.Ticks}";
                        await _memoryRepo.AddAsync(new ChatMemory
                        {
                            Key = memoryKey,
                            Content = snippet.Trim(),
                            Category = "preference",
                            SessionId = sessionId,
                            CreatedAt = DateTime.Now
                        });
                    }
                }

                // 4. 如果用户消息较短（≤20字）且没有明确规律，作为通用记忆存储
                if (userMessage.Trim().Length <= 20
                    && plates.Count == 0
                    && !VehicleBrands.Any(b => userMessage.Contains(b))
                    && !PreferenceKeywords.Any(k => userMessage.Contains(k)))
                {
                    var memoryKey = $"general_{DateTime.Now.Ticks}";
                    await _memoryRepo.AddAsync(new ChatMemory
                    {
                        Key = memoryKey,
                        Content = userMessage.Trim(),
                        Category = "general",
                        SessionId = sessionId,
                        CreatedAt = DateTime.Now
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "记忆提取失败，但不影响对话流程");
            }
        }

        /// <summary>
        /// 搜索与用户消息相关的历史记忆
        /// </summary>
        public async Task<IEnumerable<ChatMemory>> SearchRelevantMemoriesAsync(string userMessage)
        {
            if (string.IsNullOrWhiteSpace(userMessage))
                return new List<ChatMemory>();

            // 提取消息中的关键词进行搜索
            var keywords = ExtractKeywords(userMessage);
            var allMemories = new List<ChatMemory>();

            foreach (var kw in keywords)
            {
                var results = await _memoryRepo.SearchAsync(kw);
                allMemories.AddRange(results);
            }

            // 去重并限制数量
            return allMemories
                .GroupBy(m => m.Id)
                .Select(g => g.First())
                .OrderByDescending(m => m.CreatedAt)
                .Take(5);
        }

        /// <summary>
        /// 构建记忆上下文文本
        /// </summary>
        public async Task<string> BuildMemoryContextAsync(string userMessage)
        {
            var memories = await SearchRelevantMemoriesAsync(userMessage);
            if (!memories.Any())
                return string.Empty;

            var contextLines = memories.Select(m =>
                $"- [{m.Category}] {m.Content}");
            return "以下是 AI 已知的关于用户的历史信息：\n" + string.Join("\n", contextLines);
        }

        /// <summary>
        /// 获取所有记忆
        /// </summary>
        public async Task<IEnumerable<ChatMemory>> GetAllMemoriesAsync()
        {
            return await _memoryRepo.GetAllAsync();
        }

        /// <summary>
        /// 删除记忆
        /// </summary>
        public async Task<bool> DeleteMemoryAsync(int id)
        {
            return await _memoryRepo.DeleteAsync(id);
        }

        /// <summary>
        /// 插入或更新记忆（相同Key则更新内容）
        /// </summary>
        private async Task UpsertMemoryAsync(string key, string content, string category, int sessionId)
        {
            var existing = await _memoryRepo.GetByKeyAsync(key);
            if (existing != null)
            {
                existing.Content = content;
                existing.Category = category;
                await _memoryRepo.UpdateAsync(existing);
            }
            else
            {
                await _memoryRepo.AddAsync(new ChatMemory
                {
                    Key = key,
                    Content = content,
                    Category = category,
                    SessionId = sessionId,
                    CreatedAt = DateTime.Now
                });
            }
        }

        /// <summary>
        /// 从消息中提取搜索关键词
        /// </summary>
        private List<string> ExtractKeywords(string message)
        {
            var keywords = new List<string>();

            // 提取车牌号
            var plates = LicensePlateRegex.Matches(message);
            foreach (Match match in plates)
                keywords.Add(match.Value);

            // 提取车辆品牌
            foreach (var brand in VehicleBrands)
                if (message.Contains(brand))
                    keywords.Add(brand);

            // 提取较长的词作为关键词（至少2个中文字符）
            var words = Regex.Matches(message, @"[\u4e00-\u9fa5]{2,}");
            foreach (Match word in words)
                if (word.Value.Length >= 2 && !keywords.Contains(word.Value))
                    keywords.Add(word.Value);

            // 限制关键词数量
            return keywords.Take(5).ToList();
        }
    }
}
