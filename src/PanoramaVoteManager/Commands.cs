using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Extensions;
using CounterStrikeSharp.API.Modules.Utils;
using PanoramaVoteManagerAPI.Vote;

namespace PanoramaVoteManager
{
    public partial class PanoramaVoteManager
    {
        // =======================================================
        // 🆕 升級版：玩家輸入 !votemap <地圖名> 發起指定地圖的原生投票
        // =======================================================
        [ConsoleCommand("css_votemap", "玩家發起熱身賽指定地圖投票")]
        [CommandHelper(minArgs: 1, usage: "<地圖名稱>", whoCanExecute: CommandUsage.CLIENT_ONLY)] // 限制一定要輸入地圖名
        public void CommandPlayerVoteMap(CCSPlayerController? player, CommandInfo command)
        {
            if (player == null || !player.IsValid) return;

            // 1. 檢查是否為熱身賽
            var gameRulesEnt = Utilities.FindAllEntitiesByDesignerName<CBaseEntity>("cs_gamerules").SingleOrDefault();
            bool isWarmup = gameRulesEnt?.As<CCSGameRulesProxy>()?.GameRules?.WarmupPeriod == true;

            if (!isWarmup)
            {
                player.PrintToChat(" \x01[\x04伺服器\x01] \x02目前已非熱身賽期間，無法發起換圖投票！");
                return;
            }

            // 2. 檢查目前是否已有投票在排隊
            if (_currentVote != null || _votes.Count > 0 || _timeUntilNextVote > DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            {
                player.PrintToChat(" \x01[\x04伺服器\x01] \x02目前已有投票正在進行或冷卻中，請稍後再試。");
                return;
            }

            // 3. 獲取玩家輸入的地圖名稱
            string targetMap = command.GetArg(1).Trim().ToLower(); // 例如玩家打 !votemap de_dust2，這裡就會抓到 de_dust2[cite: 6]

            // 4. 安全檢查：防止玩家瞎打一些根本不存在的奇怪字串
            if (string.IsNullOrEmpty(targetMap) || targetMap.Length < 3)
            {
                player.PrintToChat(" \x01[\x04伺服器\x01] \x02請輸入正確的地圖名稱！例如: !votemap de_dust2");
                return;
            }

            // 5. 取得全伺服器所有有效玩家的 UserId 清單
            List<int> allPlayerIds = [];
            foreach (var p in Utilities.GetPlayers())
            {
                if (p != null && p.IsValid && !p.IsBot && p.UserId.HasValue)
                {
                    allPlayerIds.Add(p.UserId.Value);
                }
            }

            if (allPlayerIds.Count == 0) return;

            // 6. 動態把地圖名稱塞進 F1/F2 投票框的文字中！
            var voteTexts = new Dictionary<string, string>
            {
                { "en", $"Change map to: {targetMap}?" },
                { "zh", $"是否同意將地圖更換為：{targetMap} ？" } // 兩隊畫面上會清楚看到這個地圖名
            };

            // 7. 塞入投票佇列
            _votes.Add(new Vote(
                "#SFUI_vote_passed_changelevel", // 沿用官方原生換圖綠字提示[cite: 6, 7]
                voteTexts,
                15, // 彈出 15 秒給玩家按 F1/F2[cite: 6, 7]
                -1, // -1 代表打破隊伍限制，兩隊一起合併計票[cite: 6, 7]
                allPlayerIds,
                player.UserId ?? 99
            ));

            // 📣 聊天室全服大廣播
            Server.PrintToChatAll($" \x01[\x04投票\x01] 玩家 \x03{player.PlayerName}\x01 發起了換圖投票 ➡️ \x04{targetMap}\x01！");

            // 8. 點火！啟動官方原生投票黑框[cite: 6, 7]
            StartVote();
        }

        // ==========================================
        // ⚙️ 原本就有的後台測試指令（保留不變）[cite: 6]
        // ==========================================
        [ConsoleCommand("panoramavotemanager", "PanoramaVoteManager admin commands")]
        [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY, minArgs: 1, usage: "<command>")]
        public void CommandMapVote(CCSPlayerController player, CommandInfo command)
        {
            string subCommand = command.GetArg(1);
            switch (subCommand.ToLower(System.Globalization.CultureInfo.CurrentCulture))
            {
                case "reload":
                    Config.Reload();
                    command.ReplyToCommand(Localizer["admin.reload"]);
                    break;
                case "test":
                    if (_currentVote != null || _votes.Count > 0 || _timeUntilNextVote > DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                    {
                        int time = _votes.Sum(v => v.Time + Config.Cooldown) + (_currentVote?.Time + Config.Cooldown ?? Config.Cooldown);
                        command.ReplyToCommand(Localizer["vote.cooldown"].Value.Replace("{time}", time.ToString()));
                    }
                    Random random = new();
                    int randomTime = random.Next(3, 13);
                    _votes.Add(new Vote(
                        "#SFUI_vote_passed_changelevel",
                        new Dictionary<string, string> {
                            {"en", $"This is my cool vote -> {randomTime}"},
                            {"de", $"Mein toller Vote -> {randomTime}"},
                        },
                        randomTime,
                        -1,
                        [],
                        99
                    ));
                    StartVote();
                    break;
                default:
                    command.ReplyToCommand(Localizer["admin.unknown_command"].Value.Replace("{command}", subCommand));
                    break;
            }
        }
    }
}
