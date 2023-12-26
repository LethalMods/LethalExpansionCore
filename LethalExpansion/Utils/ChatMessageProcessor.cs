using LethalExpansion.Patches;
using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using static LethalExpansion.Utils.NetworkPacketManager;

namespace LethalExpansion.Utils
{
    internal class ChatMessageProcessor
    {
        public static bool ProcessMessage(string message)
        {
            if (!Regex.IsMatch(message, @"^\[sync\].*\[sync\]$"))
            {
                return false;
            }

            try
            {
                string content = Regex.Match(message, @"^\[sync\](.*)\[sync\]$").Groups[1].Value;

                string[] parts = content.Split('|');
                if (parts.Length != 3)
                {
                    return true;
                }

                PacketType type = (PacketType)int.Parse(parts[0]);
                string[] mid = parts[1].Split('>');
                ulong sender = ulong.Parse(mid[0]);
                long destination = long.Parse(mid[1]);
                string[] last = parts[2].Split('=');
                string header = last[0];
                string packet = last[1];

                if (destination != -1 && (ulong) destination != RoundManager.Instance.NetworkManager.LocalClientId)
                {
                    return true;
                }

                if (sender != 0)
                {
                    NetworkPacketManager.Instance.CancelTimeout((long)sender);
                }

                LethalExpansion.Log.LogInfo($"[{type}] Recieved: {message}");
                switch (type)
                {
                    case PacketType.Request:
                        ProcessRequest(sender, header, packet);
                        break;
                    case PacketType.Data:
                        ProcessData(sender, header, packet);
                        break;
                    case PacketType.Other:
                        LethalExpansion.Log.LogInfo("Unsupported type.");
                        break;
                    default:
                        LethalExpansion.Log.LogInfo("Unrecognized type.");
                        break;
                }

                return true;
            }
            catch (Exception ex)
            {
                LethalExpansion.Log.LogError(ex);
                return false;
            }
        }

        private static void ProcessClientInfoRequest(ulong sender, string header, string packet)
        {
            if (LethalExpansion.ishost || sender != 0)
            {
                return;
            }

            string configPacket = $"{LethalExpansion.ModVersion}-";
            foreach (var bundle in AssetBundlesManager.Instance.assetBundles)
            {
                configPacket += $"{bundle.Key}v{bundle.Value.Item2.GetVersion()}&";
            }
            configPacket = configPacket.Remove(configPacket.Length - 1);

            NetworkPacketManager.Instance.SendPacket(PacketType.Data, "clientinfo", configPacket, 0);
        }

        private static void ProcessHostConfigRequest(ulong sender, string header, string packet)
        {
            if (!LethalExpansion.ishost || sender == 0)
            {
                return;
            }

            NetworkPacketManager.Instance.SendPacket(NetworkPacketManager.PacketType.Request, "clientinfo", string.Empty, (long)sender);
        }

        private static void ProcessRequest(ulong sender, string header, string packet)
        {
            try
            {
                switch (header)
                {
                    // client receive info request from host
                    case "clientinfo":
                        ProcessClientInfoRequest(sender, header, packet);
                        break;
                    // host receive config request from client
                    case "hostconfig":
                        ProcessHostConfigRequest(sender, header, packet);
                        break;
                    default:
                        LethalExpansion.Log.LogInfo("Unrecognized command.");
                        break;
                }
            }
            catch (Exception ex)
            {
                LethalExpansion.Log.LogError(ex);
            }
        }

        private static void ProcessClientInfo(ulong sender, string header, string packet)
        {
            if (!LethalExpansion.ishost || sender == 0)
            {
                return;
            }

            string[] values;
            if (packet.Contains('-'))
            {
                values = packet.Split('-');
            }
            else
            {
                values = new string[1] { packet };
            }

            string bundles = string.Empty;
            foreach (var bundle in AssetBundlesManager.Instance.assetBundles)
            {
                bundles += $"{bundle.Key}v{bundle.Value.Item2.GetVersion()}&";
            }

            if (bundles.Length > 0)
            {
                bundles = bundles.Remove(bundles.Length - 1);
            }

            if (values[0] != LethalExpansion.ModVersion.ToString())
            {
                if (StartOfRound.Instance.ClientPlayerList.ContainsKey(sender))
                {
                    LethalExpansion.Log.LogError($"Kicking {sender} for wrong version.");
                    NetworkPacketManager.Instance.SendPacket(PacketType.Data, "kickreason", "Wrong version.", (long)sender);
                    StartOfRound.Instance.KickPlayer(StartOfRound.Instance.ClientPlayerList[sender]);
                }

                return;
            }
            else if (values.Length > 1 && values[1] != bundles)
            {
                if (StartOfRound.Instance.ClientPlayerList.ContainsKey(sender))
                {
                    LethalExpansion.Log.LogError($"Kicking {sender} for wrong bundles.");
                    NetworkPacketManager.Instance.SendPacket(PacketType.Data, "kickreason", "Wrong bundles.", (long)sender);
                    StartOfRound.Instance.KickPlayer(StartOfRound.Instance.ClientPlayerList[sender]);
                }

                return;
            }

            NetworkPacketManager.Instance.SendPacket(PacketType.Data, "hostconfig", String.Empty, (long)sender);
        }

        private static void ProcessHostConfig(ulong sender, string header, string packet)
        {
            if (LethalExpansion.ishost || sender != 0)
            {
                return;
            }

            LethalExpansion.hostDataWaiting = false;
        }

        private static void ProcessKickReason(ulong sender, string header, string packet)
        {
            if (LethalExpansion.ishost || sender != 0)
            {
                return;
            }

            LethalExpansion.lastKickReason = packet;
        }

        private static void ProcessData(ulong sender, string header, string packet)
        {
            try
            {
                switch (header)
                {
                    // host receive info from client
                    case "clientinfo":
                        ProcessClientInfo(sender, header, packet);
                        break;
                    // client receive config from host
                    case "hostconfig":
                        ProcessHostConfig(sender, header, packet);
                        break;
                    case "kickreason":
                        ProcessKickReason(sender, header, packet);
                        break;
                    default:
                        LethalExpansion.Log.LogInfo("Unrecognized property.");
                        break;
                }
            }
            catch (Exception ex)
            {
                LethalExpansion.Log.LogError(ex);
            }
        }
    }
}
