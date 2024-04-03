using COSML;
using COSML.Log;
using LiteNetLib;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace COSMP.Network.Core
{
    internal abstract class NetworkManager : INetEventListener
    {
        internal PlayerData Data;

        private readonly Dictionary<short, PlayerData> players = [];
        internal PlayerData[] Players { get => [.. players.Values.OrderBy(p => p.Id)]; }

        protected Loggable Log;
        protected GlobalData Settings;
        protected NetManager manager;
        protected PlayersManager playersManager;

        protected bool connected;
        internal bool IsConnected() => connected;

        private readonly Task poolTask;
        private readonly CancellationTokenSource cancelTokenSource;
        private readonly CancellationToken cancelToken;
        private readonly HashSet<PlayerData> playersToAdd;
        private readonly HashSet<PlayerData> playersToRemove;

        protected NetworkManager(GlobalData settings)
        {
            connected = false;
            Log = new SimpleLogger($"COSMP]:[{GetType().Name}");
            Settings = settings;
            manager = new NetManager(this)
            {
                DisconnectTimeout = COSMPConstants.NETWORK_TIMEOUT,
                PingInterval = COSMPConstants.NETWORK_PING
            };
            Data = new() { Username = Settings.username };

            cancelTokenSource = new();
            cancelToken = cancelTokenSource.Token;
            poolTask = new Task(() =>
            {
                while (!cancelToken.IsCancellationRequested)
                {
                    manager.PollEvents();
                    Thread.Sleep(15);
                }
            }, cancelToken);
            poolTask.Start();

            playersToAdd = [];
            playersToRemove = [];

            GameObject playersManagerGo = new("COSMP-PlayersManager");
            playersManager = playersManagerGo.AddComponent<PlayersManager>();

            On.PlayerController.Init += OnPlayerControllerInit;
            On.PlayerController.Loop += OnPlayerControllerLoop;
            On.PlayerController.PhysicLoop += OnPlayerControllerPhysicLoop;
            //On.HumanoidMove.MoveTo += OnHumanoidMoveMoveTo;
            On.HumanoidMove.ChangeSpot_AbstractSpot_bool_bool_bool += OnHumanoidMoveChangeSpot;
            On.HumanoidMove.Stop_bool_bool_bool_bool += OnHumanoidMoveStop;
            On.HumanoidMove.LookAt_Vector3_bool += OnHumanoidMoveLookAt;
            On.HumanoidMove.StartAction += OnHumanoidMoveStartAction;
            On.PauseController.EnterPause += OnPauseControllerEnterPause;
            On.PauseController.ExitPause += OnPauseControllerExitPause;
            On.PlaceController.BackToTitle += OnBackToTitle;
        }

        internal void Stop()
        {
            cancelTokenSource.Cancel();
            cancelTokenSource.Dispose();
            manager.Stop();
            connected = false;

            On.PlayerController.Init -= OnPlayerControllerInit;
            On.PlayerController.Loop -= OnPlayerControllerLoop;
            On.PlayerController.PhysicLoop -= OnPlayerControllerPhysicLoop;
            //On.HumanoidMove.MoveTo -= OnHumanoidMoveMoveTo;
            On.HumanoidMove.ChangeSpot_AbstractSpot_bool_bool_bool -= OnHumanoidMoveChangeSpot;
            On.HumanoidMove.Stop_bool_bool_bool_bool -= OnHumanoidMoveStop;
            On.HumanoidMove.LookAt_Vector3_bool -= OnHumanoidMoveLookAt;
            On.HumanoidMove.StartAction -= OnHumanoidMoveStartAction;
            On.PauseController.EnterPause -= OnPauseControllerEnterPause;
            On.PauseController.ExitPause -= OnPauseControllerExitPause;
            On.PlaceController.BackToTitle -= OnBackToTitle;

            foreach (PlayerData data in Players)
            {
                RemovePlayer(data.Id);
            }
            UnityEngine.Object.Destroy(playersManager.gameObject);
        }

        protected NetPeer GetPlayerPeer(int id)
        {
            PlayerData data = GetPlayerData(id);
            if (data == null) return null;

            return manager.ConnectedPeerList.Find(p => p.Id == id);
        }

        internal PlayerData AddPlayer(int id, PlayerData player)
        {
            player.Id = (short)id;
            players.Add(player.Id, player);

            if (player.Id != Data.Id)
            {
                playersToAdd.Add(player);
            }

            return player;
        }
        internal PlayerData AddPlayer(int id, string username) => AddPlayer(id, new PlayerData { Id = (short)id, Username = username });
        internal PlayerData AddPlayer(PlayerMeta meta) => AddPlayer(meta.Id, new PlayerData { Id = meta.Id, Username = meta.Username, Position = meta.Position });

        internal bool RemovePlayer(int id)
        {
            PlayerData player = GetPlayerData(id);
            if (player == null) return false;

            if (player.Id != Data.Id)
            {
                playersToRemove.Add(player);
            }

            return players.Remove((short)id);
        }

        internal PlayerData GetPlayerData(int id)
        {
            if (!players.ContainsKey((short)id)) return null;
            return players[(short)id];
        }

        protected void UpdatePlayerPosition(PlayerData data) => playersManager.MovePlayer(data);
        protected void UpdatePlayerLook(PlayerData data) => playersManager.LookPlayer(data);
        protected void UpdatePlayerAction(PlayerData data) => playersManager.ActionPlayer(data);
        protected void UpdatePlayerCanvasAction(PlayerData data, PlayerCanvasAction action) => playersManager.CanvasActionPlayer(data, action);

        protected void UpdatePositionData()
        {
            Data.Position.Place = PlayersManager.GetCurrentPlace();
            HumanoidMove humanoidMove = GameController.GetInstance().GetPlayerController().GetHumanoid();
            Data.Position.Position = humanoidMove.transform.position;
            Vector3 destination = humanoidMove.GetDestination();
            if (destination.x == 0f || destination.y == 0f || destination.z == 0f) destination = Data.Position.Position;
            if (float.IsInfinity(destination.x) || float.IsInfinity(destination.y) || float.IsInfinity(destination.z)) destination = Data.Position.Position;
            Data.Position.Destination = destination;
            Log.Debug($"UpdatePositionData: {Data.Position}");
            OnPlayerMove();
        }

        private void OnPlayerControllerInit(On.PlayerController.orig_Init orig, PlayerController self, Portal portal)
        {
            orig(self, portal);
            if (!PlayersManager.IsMainPlayerController(self)) return;

            Log.Debug("OnPlayerControllerInit");
            Data.Position.Place = ReflectionHelper.GetField<Portal, Place>(portal, "portalPlace")?.gameObject.scene.name;
            Data.Position.Run = false;
            Data.Position.Position = portal.GetExit(true);
            Data.Position.Destination = portal.GetEnter();
            Log.Debug($"pos: {Data.Position}");
            OnPlayerMove();
        }

        private void OnPlayerControllerLoop(On.PlayerController.orig_Loop orig, PlayerController self)
        {
            orig(self);
            if (!PlayersManager.IsMainPlayerController(self)) return;

            foreach (PlayerData data in playersToAdd) playersManager.Add(data);
            playersToAdd.Clear();

            foreach (PlayerData data in playersToRemove) playersManager.Remove(data);
            playersToRemove.Clear();

            playersManager.Loop();
        }

        private void OnPlayerControllerPhysicLoop(On.PlayerController.orig_PhysicLoop orig, PlayerController self)
        {
            orig(self);
            if (!PlayersManager.IsMainPlayerController(self)) return;

            playersManager.PhysicLoop();
        }

        //protected abstract void OnPlayerMove();
        //private void OnHumanoidMoveMoveTo(On.HumanoidMove.orig_MoveTo orig, HumanoidMove self, Vector3 destination, bool keepOnAngle, bool keepIsDown)
        //{
        //    orig(self, destination, keepOnAngle, keepIsDown);

        //    if (PlayersManager.IsMainHumanoidMove(self))
        //    {
        //        AbstractSpot spot = ReflectionHelper.GetField<HumanoidMove, AbstractSpot>(self, "spot");
        //        if (spot is InteractionSpot interactionSpot) destination = interactionSpot.GetTargetPosition();

        //        bool run = ReflectionHelper.GetField<HumanoidMove, bool>(self, "run");
        //        Log.Debug($"OnHumanoidMoveMoveTo: {Data.Position} {destination} {run}");
        //        UpdatePositionData(destination);
        //    }
        //}

        protected abstract void OnPlayerMove();
        private bool OnHumanoidMoveChangeSpot(On.HumanoidMove.orig_ChangeSpot_AbstractSpot_bool_bool_bool orig, HumanoidMove self, AbstractSpot changeSpot, bool forceWalk, bool init, bool resetAnimation)
        {
            bool ret = orig(self, changeSpot, forceWalk, init, resetAnimation);

            if (PlayersManager.IsMainHumanoidMove(self))
            {
                Data.Position.Place = PlayersManager.GetCurrentPlace();
                Data.Position.Run = changeSpot.ForceRun();
                Data.Position.Position = self.transform.position;

                Vector3 destination = changeSpot.GetTargetPosition();
                if (destination.x == 0f || destination.y == 0f || destination.z == 0f) destination = Data.Position.Position;
                if (float.IsInfinity(destination.x) || float.IsInfinity(destination.y) || float.IsInfinity(destination.z)) destination = Data.Position.Position;
                Data.Position.Destination = destination;

                OnPlayerMove();

            }

            return ret;
        }

        private void OnHumanoidMoveStop(On.HumanoidMove.orig_Stop_bool_bool_bool_bool orig, HumanoidMove self, bool newOnAngle, bool newDown, bool resetSpot, bool resetVelocity)
        {
            orig(self, newOnAngle, newDown, resetSpot, resetVelocity);

            if (!PlayersManager.IsMainHumanoidMove(self)) return;

            Data.Position.Position = self.transform.position;
            Data.Position.Destination = self.transform.position;
            OnPlayerMove();
        }

        protected abstract void OnPlayerLook();
        private void OnHumanoidMoveLookAt(On.HumanoidMove.orig_LookAt_Vector3_bool orig, HumanoidMove self, Vector3 lookPosition, bool resetTarget)
        {
            orig(self, lookPosition, resetTarget);

            if (!PlayersManager.IsMainHumanoidMove(self)) return;

            Data.Look = lookPosition;
            OnPlayerLook();
        }

        protected abstract void OnPlayerAction();
        private void OnHumanoidMoveStartAction(On.HumanoidMove.orig_StartAction orig, HumanoidMove self, HumanoidActionType actionType, HumanoidAnchor newActionAnchor, bool stayInPosition)
        {
            orig(self, actionType, newActionAnchor, stayInPosition);

            if (!PlayersManager.IsMainHumanoidMove(self)) return;

            Data.Action = actionType;
            OnPlayerAction();
        }

        protected abstract void OnPlayerCanvasAction(PlayerCanvasAction action);
        private void OnPauseControllerExitPause(On.PauseController.orig_ExitPause orig, PauseController self)
        {
            orig(self);

            foreach (PlayerData data in Players)
            {
                UpdatePlayerPosition(data);
            }

            OnPlayerCanvasAction(PlayerCanvasAction.None);
        }

        private void OnPauseControllerEnterPause(On.PauseController.orig_EnterPause orig, PauseController self, bool journal, bool inventory, bool pauseSound)
        {
            orig(self, journal, inventory, pauseSound);

            PlayerCanvasAction action = PlayerCanvasAction.Pause;
            if (journal) action = PlayerCanvasAction.Journal;
            if (inventory) action = PlayerCanvasAction.Inventory;
            OnPlayerCanvasAction(action);
        }

        private void OnBackToTitle(On.PlaceController.orig_BackToTitle orig, PlaceController self)
        {
            orig(self);

            Data.Position.Place = null;
            OnPlayerMove();
        }

        protected abstract void OnConnect(NetPeer peer);
        void INetEventListener.OnPeerConnected(NetPeer peer) => OnConnect(peer);

        protected abstract void OnDisconnect(NetPeer peer, DisconnectInfo disconnectInfo);
        void INetEventListener.OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo) => OnDisconnect(peer, disconnectInfo);

        protected abstract void OnReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod);
        void INetEventListener.OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod) => OnReceive(peer, reader, channelNumber, deliveryMethod);

        protected abstract void OnPing(NetPeer peer, int latency);
        void INetEventListener.OnNetworkLatencyUpdate(NetPeer peer, int latency) => OnPing(peer, latency);

        protected abstract void OnConnectionRequest(ConnectionRequest request);
        void INetEventListener.OnConnectionRequest(ConnectionRequest request) => OnConnectionRequest(request);

        void INetEventListener.OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) { }

        void INetEventListener.OnNetworkError(IPEndPoint endPoint, SocketError errorCode)
        {
            Log.Error($"An error occurred: {errorCode}");
        }
    }
}
