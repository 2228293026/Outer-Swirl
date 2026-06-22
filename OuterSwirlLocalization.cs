using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Outer_Swirl
{
    internal static class OuterSwirlLocalization
    {
        private static readonly Dictionary<string, string> _localizationDict = new();

        public static void RegisterLocalization(string json)
        {
            try
            {
                var entries = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, string>>>(json);
                if (entries == null) { Debug.Log("[OuterSwirl] RL: entries null"); return; }

                // 获取当前语言代码
                string langCode = GetCurrentLanguageCode();

                // 清空旧字典（避免重复加载时累积）
                _localizationDict.Clear();

                foreach (var kvp in entries)
                {
                    if (kvp.Value.TryGetValue(langCode, out string val))
                        _localizationDict[kvp.Key] = val;
                    else if (kvp.Value.TryGetValue("en", out val))
                        _localizationDict[kvp.Key] = val;
                    // 如果连英文都没有，跳过
                }

                Debug.Log($"[OuterSwirl] Localization loaded: {_localizationDict.Count} entries");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[OuterSwirl] Localization load error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static string GetCurrentLanguageCode()
        {
            // 根据游戏当前语言映射到 ISO 代码
            switch (RDString.language)
            {
                case SystemLanguage.ChineseSimplified:
                case SystemLanguage.Chinese:
                    return "zh";
                case SystemLanguage.Korean:
                    return "ko";
                case SystemLanguage.Japanese:
                    return "ja";
                default:
                    return "en";
            }
        }

        // 供补丁调用的公共方法
        internal static bool TryGetLocalizedString(string key, out string value)
        {
            return _localizationDict.TryGetValue(key, out value);
        }
    }
}
