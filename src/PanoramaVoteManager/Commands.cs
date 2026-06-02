using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Extensions;
using PanoramaVoteManagerAPI.Vote;

namespace PanoramaVoteManager
{
    public partial class PanoramaVoteManager
    {
        [ConsoleCommand("css_vt", "玩家發起熱身賽指定地圖投票")]
        [CommandHelper(minArgs: 1, usage: "<地圖名稱>", whoCanExecute: CommandUsage.CLIENT_ONLY)]
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

            // 2. 檢查冷卻與佇列
            if (_currentVote != null || _votes.Count > 0 || _timeUntilNextVote > DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            {
                player.PrintToChat(" \x01[\x04伺服器\x01] \x02目前已有投票正在進行或冷卻中。");
                return;
            }

            string targetMap = command.GetArg(1).Trim().ToLower();
            
            // 3. 準備玩家清單
            List<int> allPlayerIds = Utilities.GetPlayers().Where(p => p != null && p.IsValid && !p.IsBot && p.UserId.HasValue).Select(p => p.UserId!.Value).ToList();

            // 4. 【重點】完全依照原生 test 指令的 6 參數構造
            _votes.Add(new Vote(
                "#SFUI_vote_passed_changelevel",
                new Dictionary<string, string> {
                    {"en", $"Change to: {targetMap}?"},
                    {"zh", $"是否更換地圖至: {targetMap}?"}
                },
                15, // 時間
                -1, // 隊伍
                allPlayerIds,
                player.UserId ?? 99
            ));

            // 5. 啟動投票
            StartVote();
            Server.PrintToChatAll($" \x01[\x04投票\x01] 玩家 \x03{player.PlayerName}\x01 發起了換圖投票 ➡️ \x04{targetMap}\x01！");
        }

        // 以下保留原生指令
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
