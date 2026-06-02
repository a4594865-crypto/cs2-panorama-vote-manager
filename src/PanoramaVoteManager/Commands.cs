using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Extensions;

namespace PanoramaVoteManager
{
    public partial class PanoramaVoteManager
    {
        // =======================================================
        // 🎮 玩家專用聊天室指令：.vt <地圖名> 或 !vt <地圖名>
        // =======================================================
        [ConsoleCommand("css_vt", "玩家發起熱身賽指定地圖投票")]
        [CommandHelper(minArgs: 1, usage: "<地圖名稱>", whoCanExecute: CommandUsage.CLIENT_ONLY)]
        public void CommandPlayerVoteMap(CCSPlayerController? player, CommandInfo command)
        {
            if (player == null || !player.IsValid) return;

            // 1. 安全審查：確保目前是熱身賽（Warmup）
            var gameRulesEnt = Utilities.FindAllEntitiesByDesignerName<CBaseEntity>("cs_gamerules").SingleOrDefault();
            bool isWarmup = gameRulesEnt?.As<CCSGameRulesProxy>()?.GameRules?.WarmupPeriod == true;

            if (!isWarmup)
            {
                player.PrintToChat(" \x01[\x04伺服器\x01] \x02目前已非熱身賽期間，無法發起換圖投票！");
                return;
            }

            // 2. 檢查目前是否已有投票在排隊或冷卻中
            if (_currentVote != null || _votes.Count > 0 || _timeUntilNextVote > DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            {
                player.PrintToChat(" \x01[\x04伺服器\x01] \x02目前已有投票正在進行或冷卻中，請稍後再試。");
                return;
            }

            // 3. 獲取玩家輸入的地圖名稱並轉小寫防呆
            string targetMap = command.GetArg(1).Trim().ToLower();

            if (string.IsNullOrEmpty(targetMap) || targetMap.Length < 3)
            {
                player.PrintToChat(" \x01[\x04伺服器\x01] \x02請輸入正確的地圖名稱！例如: .vt de_dust2");
                return;
            }

            // 4. 撈出全伺服器所有有效玩家的 UserId（打破隊伍限制合併計票）
            List<int> allPlayerIds = [];
            foreach (var p in Utilities.GetPlayers())
            {
                if (p != null && p.IsValid && !p.IsBot && p.UserId.HasValue)
                {
                    allPlayerIds.Add(p.UserId.Value);
                }
            }

            if (allPlayerIds.Count == 0) return;

            // 5. 定義官方 F1/F2 原生 UI 畫面上要顯示的提示文字
            var voteTexts = new Dictionary<string, string>
            {
                { "en", $"Change map to: {targetMap}?" },
                { "zh", $"是否同意將地圖更換為：{targetMap} ？" }
            };

            // 6. 💡【徹底修復：完全符合官方原生的 6 個參數規格】
            // 移除了導致編譯器瞎眼報錯的第 7 個 Callback 參數
            var myMapVote = new PanoramaVoteManagerAPI.Vote.Vote(
                "#SFUI_vote_passed_changelevel", 
                voteTexts,
                15, // 官方黑框顯示 15 秒供玩家按鍵
                -1, // -1 代表打破隊伍限制，兩隊一起合併計票
                allPlayerIds,
                (int)(player.UserId ?? 99)
            );

            _votes.Add(myMapVote);

            // 全服聊天室通知是誰點火發起
            Server.PrintToChatAll($" \x01[\x04投票\x01] 玩家 \x03{player.PlayerName}\x01 發起了換圖投票 ➡️ \x04{targetMap}\x01！");

            // 7. 啟動這款插件原生的投票功能
            StartVote();

            // 🚀【後台同步監聽計票】：
            // 既然不能把邏輯寫在 Vote 的參數裡，我們直接啟動背景工作延遲 16 秒（15秒投票 + 1秒緩衝時間）
            // 時間一到，我們直接從剛才建立的 myMapVote 物件裡撈出按 F1 和 F2 的最終計票結果！
            Task.Run(async () =>
            {
                await Task.Delay(16000); // 延遲 16000 毫秒 (16秒)
                Server.NextFrame(() =>
                {
                    // 如果按 F1 (Yes) 的票數大於按 F2 (No) 的票數，代表投票通過！
                    if (myMapVote.YesVotes > myMapVote.NoVotes)
                    {
                        Server.PrintToChatAll($" \x01[\x04投票\x01] \x05換圖投票通過！伺服器將在 3 秒後切換至地圖: \x04{targetMap}");
                        
                        // 倒數 3 秒執行控制台換圖指令
                        Task.Run(async () =>
                        {
                            await Task.Delay(3000);
                            Server.NextFrame(() => {
                                Server.ExecuteCommand($"changelevel {targetMap}");
                            });
                        });
                    }
                    else
                    {
                        Server.PrintToChatAll(" \x01[\x04投票\x01] \x02換圖投票未通過（同意票不足或平票）或已被取消。");
                    }
                });
            });
        }

        // =======================================================
        // ⚙️ 官方原生自帶的後台重載/測試指令（完全保留，一字未改）
        // =======================================================
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
                    _votes.Add(new PanoramaVoteManagerAPI.Vote.Vote(
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
