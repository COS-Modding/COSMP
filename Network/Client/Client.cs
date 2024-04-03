using COSML;
using COSMP.Network.Core;
using LiteNetLib;
using LiteNetLib.Utils;
using System;
using System.Net.Sockets;

namespace COSMP.Network.Client
{
    internal class Client : NetworkManager
    {
        internal event Action LoginHook;
        internal event Action<PlayerData> JoinHook;
        internal event Action<PlayerData> LeaveHook;
        internal event Action<PlayerData> PingHook;
        internal event Action<PlayerData> PositionHook;
        internal event Action MetaHook;
        internal event Action<string, NetworkErrorCode> DisconnectHook;

        private NetPeer serverPeer;

        internal Client(GlobalData settings) : base(settings)
        {
            connected = false;
            Log.Info($"Connecting to {settings.host}:{settings.port}");
            manager.Start();
            _ = manager.Connect(settings.host, settings.port, COSMPConstants.SERVER_KEY) ?? throw new Exception();
        }

        protected override void OnReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
        {
            PacketType type = PacketType.Unknown;
            try
            {
                type = (PacketType)reader.GetByte();

                switch (type)
                {
                    case PacketType.Login:
                        {
                            PlayerErrorCode code = (PlayerErrorCode)reader.GetByte();
                            if (code > PlayerErrorCode.Success)
                            {
                                string reason = "cosmp.error.unknown";
                                switch (code)
                                {
                                    case PlayerErrorCode.UsernameTaken:
                                        reason = "cosmp.error.username.taken";
                                        break;

                                    case PlayerErrorCode.Ban:
                                        reason = "cosmp.error.banned";
                                        break;
                                }
                                DisconnectHook?.Invoke(I18n.Get(reason), NetworkErrorCode.Connect);
                                break;
                            }
                            connected = true;
                            AddPlayer(peer.RemoteId, Data);
                            if (PlayersManager.IsInGame()) UpdatePositionData();
                            Log.Info("Logged successfully");
                            LoginHook?.Invoke();

                            break;
                        }
                    case PacketType.Meta:

                        {
                            PlayerList list = reader.Get<PlayerList>();
                            foreach (PlayerMeta meta in list)
                            {
                                Log.Debug($"meta: {meta.Id} {meta.Username} {meta.Position}");
                                AddPlayer(meta);
                            }
                            MetaHook?.Invoke();

                            break;
                        }

                    case PacketType.Join:
                        {
                            short id = reader.GetShort();
                            string username = reader.GetString();
                            PlayerData data = AddPlayer(id, username);
                            JoinHook?.Invoke(data);

                            break;
                        }

                    case PacketType.Leave:
                        {
                            short id = reader.GetShort();
                            PlayerData data = GetPlayerData(id);
                            if (data == null) break;

                            RemovePlayer(id);
                            LeaveHook?.Invoke(data);

                            break;
                        }

                    case PacketType.Ping:
                        {
                            short id = reader.GetShort();
                            PlayerData data = GetPlayerData(id);
                            if (data == null) break;

                            short ping = reader.GetShort();
                            data.Ping = ping;
                            PingHook?.Invoke(data);

                            break;
                        }

                    case PacketType.Position:
                        {
                            PlayerData data = GetPlayerData(reader.GetShort());
                            if (data == null) break;

                            data.Position = reader.Get<PositionData>();

                            if (!PlayersManager.IsInGame()) break;

                            UpdatePlayerPosition(data);
                            PositionHook?.Invoke(data);

                            break;
                        }

                    case PacketType.Look:
                        {
                            PlayerData data = GetPlayerData(reader.GetShort());
                            if (data == null) break;

                            data.Look = reader.Get<Vector3Data>();

                            if (!PlayersManager.IsInGame()) break;

                            UpdatePlayerLook(data);

                            break;
                        }

                    case PacketType.Action:
                        {
                            PlayerData data = GetPlayerData(reader.GetShort());
                            if (data == null) break;

                            data.Action = (HumanoidActionType)reader.GetByte();

                            if (!PlayersManager.IsInGame()) break;

                            UpdatePlayerAction(data);

                            break;
                        }

                    case PacketType.CanvasAction:
                        {
                            PlayerData data = GetPlayerData(reader.GetShort());
                            if (data == null) break;

                            PlayerCanvasAction action = (PlayerCanvasAction)reader.GetByte();

                            if (!PlayersManager.IsInGame()) break;

                            UpdatePlayerCanvasAction(data, action);

                            break;
                        }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to parse packet \"{type}\":\n{ex}");
            }
        }

        protected override void OnPlayerMove()
        {
            SendPosition(Data.Position);
        }

        protected override void OnPlayerLook()
        {
            SendLook(Data.Look);
        }

        protected override void OnPlayerAction()
        {
            SendAction(Data.Action);
        }

        protected override void OnPlayerCanvasAction(PlayerCanvasAction action)
        {
            SendCanvasAction(action);
        }

        private void SendLogin(string username)
        {
            NetDataWriter writer = new();
            writer.Put((byte)PacketType.Login);
            writer.Put(username);
            serverPeer.Send(writer, DeliveryMethod.ReliableOrdered);
        }

        private void SendPosition(PositionData position)
        {
            NetDataWriter writer = new();
            writer.Put((byte)PacketType.Position);
            writer.Put(position);
            serverPeer.Send(writer, DeliveryMethod.ReliableSequenced);
        }

        private void SendLook(Vector3Data look)
        {
            NetDataWriter writer = new();
            writer.Put((byte)PacketType.Look);
            writer.Put(look);
            serverPeer.Send(writer, DeliveryMethod.ReliableSequenced);
        }

        private void SendAction(HumanoidActionType action)
        {
            NetDataWriter writer = new();
            writer.Put((byte)PacketType.Action);
            writer.Put((byte)action);
            serverPeer.Send(writer, DeliveryMethod.ReliableSequenced);
        }

        private void SendCanvasAction(PlayerCanvasAction action)
        {
            NetDataWriter writer = new();
            writer.Put((byte)PacketType.CanvasAction);
            writer.Put((byte)action);
            serverPeer.Send(writer, DeliveryMethod.ReliableSequenced);
        }

        protected override void OnConnect(NetPeer peer)
        {
            serverPeer = peer;
            Log.Info($"Connected, sending login request");
            SendLogin(Data.Username);
        }

        protected override void OnDisconnect(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            string reason = $"{disconnectInfo.Reason}";
            if (disconnectInfo.SocketErrorCode != SocketError.Success) reason = $"{disconnectInfo.Reason}({disconnectInfo.SocketErrorCode})";
            if (!disconnectInfo.AdditionalData.IsNull)
            {
                NetworkErrorCode code = (NetworkErrorCode)disconnectInfo.AdditionalData.GetByte();
                if (code == NetworkErrorCode.Kick) reason = I18n.Get("cosmp.error.kick");
                else if (code == NetworkErrorCode.Ban) reason = I18n.Get("cosmp.error.ban");
            }
            Log.Info($"You have been disconnected: {reason}");
            DisconnectHook?.Invoke(reason, NetworkErrorCode.Disconnect);
            connected = false;
        }

        protected override void OnPing(NetPeer peer, int latency)
        {
            Data.Ping = (short)latency;
            PingHook?.Invoke(Data);
        }

        protected override void OnConnectionRequest(ConnectionRequest request) => request.Reject();
    }
}
