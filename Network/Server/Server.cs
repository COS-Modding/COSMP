using COSMP.Network.Core;
using LiteNetLib;
using LiteNetLib.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace COSMP.Network.Server
{
    internal class Server : NetworkManager
    {
        internal event Action<PlayerData> JoinHook;
        internal event Action<PlayerData> LeaveHook;
        internal event Action<PlayerData> PingHook;
        internal event Action<PlayerData> PositionHook;

        private readonly HashSet<int> connectedPeers = [];

        internal Server(GlobalData settings) : base(settings)
        {
            Log.Info($"Starting server");
            bool started = manager.Start(COSMPConstants.DEFAULT_SERVER_PORT);
            if (!started) throw new Exception();
            connected = true;
            AddPlayer(-1, Data);
            Log.Info($"Server started");
        }

        internal bool Kick(short id)
        {
            NetPeer peer = GetPlayerPeer(id);
            if (peer == null) return false;

            NetDataWriter writer = new();
            writer.Put((byte)NetworkErrorCode.Kick);
            peer.Disconnect(writer);

            return true;
        }

        internal bool Ban(short id)
        {
            NetPeer peer = GetPlayerPeer(id);
            if (peer == null) return false;

            Settings.banned.Add(peer.Address.ToString());

            NetDataWriter writer = new();
            writer.Put((byte)NetworkErrorCode.Ban);
            peer.Disconnect(writer);

            return true;
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
                            string username = reader.GetString();
                            if (Players.Where(p => p.Username == username).Count() > 0)
                            {
                                SendLoginError(peer, PlayerErrorCode.UsernameTaken);
                                break;
                            }
                            else if (Settings.banned.Contains(peer.Address.ToString()))
                            {
                                SendLoginError(peer, PlayerErrorCode.Ban);
                                break;
                            }
                            if (PlayersManager.IsInGame()) UpdatePositionData();
                            SendLogin(peer);
                            connectedPeers.Add(peer.Id);
                            short id = (short)peer.Id;
                            BroadcastJoin(id, username, peer);
                            PlayerData data = AddPlayer(id, username);
                            JoinHook?.Invoke(data);
                            Log.Info($"{data.Username} logged in");

                            break;
                        }

                    case PacketType.Position:
                        {
                            PlayerData data = GetPlayerData(peer.Id);
                            if (data == null) break;

                            data.Position = reader.Get<PositionData>();
                            BroadcastPosition(data.Id, data.Position, peer);

                            if (!PlayersManager.IsInGame()) break;

                            UpdatePlayerPosition(data);
                            PositionHook?.Invoke(data);

                            break;
                        }

                    case PacketType.Look:
                        {
                            PlayerData data = GetPlayerData(peer.Id);
                            if (data == null) break;

                            data.Look = reader.Get<Vector3Data>();
                            BroadcastLook(data.Id, data.Look, peer);

                            if (!PlayersManager.IsInGame()) break;

                            UpdatePlayerLook(data);

                            break;
                        }

                    case PacketType.Action:
                        {
                            PlayerData data = GetPlayerData(peer.Id);
                            if (data == null) break;

                            data.Action = (HumanoidActionType)reader.GetByte();
                            BroadcastAction(data.Id, data.Action, peer);

                            if (!PlayersManager.IsInGame()) break;

                            UpdatePlayerAction(data);

                            break;
                        }

                    case PacketType.CanvasAction:
                        {
                            PlayerData data = GetPlayerData(peer.Id);
                            if (data == null) break;

                            PlayerCanvasAction action = (PlayerCanvasAction)reader.GetByte();
                            BroadcastCanvasAction(data.Id, action, peer);

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
            BroadcastPosition(Data.Id, Data.Position);
        }

        protected override void OnPlayerLook()
        {
            BroadcastLook(Data.Id, Data.Look);
        }

        protected override void OnPlayerAction()
        {
            BroadcastAction(Data.Id, Data.Action);
        }

        protected override void OnPlayerCanvasAction(PlayerCanvasAction action)
        {
            BroadcastCanvasAction(Data.Id, action);
        }

        private void SendLogin(NetPeer peer)
        {
            NetDataWriter writer = new();
            writer.Put((byte)PacketType.Login);
            writer.Put((byte)PlayerErrorCode.Success);
            peer.Send(writer, DeliveryMethod.ReliableOrdered);

            writer.Reset();
            writer.Put((byte)PacketType.Meta);
            writer.Put(new PlayerList(Players));
            peer.Send(writer, DeliveryMethod.ReliableSequenced);
        }

        private void SendLoginError(NetPeer peer, PlayerErrorCode errorCode)
        {
            NetDataWriter writer = new();
            writer.Put((byte)PacketType.Login);
            writer.Put((byte)errorCode);
            peer.Send(writer, DeliveryMethod.ReliableOrdered);
        }

        private void BroadcastJoin(short id, string username, NetPeer excluded = null)
        {
            NetDataWriter writer = new();
            writer.Put((byte)PacketType.Join);
            writer.Put(id);
            writer.Put(username);
            Broadcast(writer, DeliveryMethod.ReliableOrdered, excluded);
        }

        private void BroadcastLeave(short id, NetPeer excluded = null)
        {
            NetDataWriter writer = new();
            writer.Put((byte)PacketType.Leave);
            writer.Put(id);
            Broadcast(writer, DeliveryMethod.ReliableOrdered, excluded);
        }

        private void BroadcastPing(short id, short ping, NetPeer excluded = null)
        {
            NetDataWriter writer = new();
            writer.Put((byte)PacketType.Ping);
            writer.Put(id);
            writer.Put(ping);
            Broadcast(writer, DeliveryMethod.ReliableSequenced, excluded);
        }

        private void BroadcastPosition(short id, PositionData position, NetPeer excluded = null)
        {
            NetDataWriter writer = new();
            writer.Put((byte)PacketType.Position);
            writer.Put(id);
            writer.Put(position);
            Broadcast(writer, DeliveryMethod.ReliableSequenced, excluded);
        }

        private void BroadcastLook(short id, Vector3Data look, NetPeer excluded = null)
        {
            NetDataWriter writer = new();
            writer.Put((byte)PacketType.Look);
            writer.Put(id);
            writer.Put(look);
            Broadcast(writer, DeliveryMethod.ReliableSequenced, excluded);
        }

        private void BroadcastAction(short id, HumanoidActionType action, NetPeer excluded = null)
        {
            NetDataWriter writer = new();
            writer.Put((byte)PacketType.Action);
            writer.Put(id);
            writer.Put((byte)action);
            Broadcast(writer, DeliveryMethod.ReliableSequenced, excluded);
        }

        private void BroadcastCanvasAction(short id, PlayerCanvasAction action, NetPeer excluded = null)
        {
            NetDataWriter writer = new();
            writer.Put((byte)PacketType.CanvasAction);
            writer.Put(id);
            writer.Put((byte)action);
            Broadcast(writer, DeliveryMethod.ReliableSequenced, excluded);
        }

        private void Broadcast(NetDataWriter writer, DeliveryMethod deliveryMethod, NetPeer excluded = null)
        {
            foreach (NetPeer peer in manager.ConnectedPeerList)
            {
                if (excluded != null && (excluded.Id == peer.Id || !connectedPeers.Contains(excluded.Id))) continue;
                peer.Send(writer, deliveryMethod);
            }
        }

        protected override void OnConnect(NetPeer peer) { }

        protected override void OnDisconnect(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            Log.Info($"Client {peer.Id} disconnected with reason {disconnectInfo.Reason}");
            PlayerData data = GetPlayerData(peer.Id);
            if (data == null) return;

            if (RemovePlayer(data.Id))
            {
                BroadcastLeave(data.Id, peer);
                connectedPeers.Remove(peer.Id);
                LeaveHook?.Invoke(data);
            }
        }

        protected override void OnPing(NetPeer peer, int latency)
        {
            PlayerData data = GetPlayerData(peer.Id);
            if (data == null) return;

            data.Ping = (short)latency;
            PingHook?.Invoke(data);
            BroadcastPing(data.Id, data.Ping, peer);
        }

        protected override void OnConnectionRequest(ConnectionRequest request) => request.AcceptIfKey(COSMPConstants.SERVER_KEY);
    }
}
