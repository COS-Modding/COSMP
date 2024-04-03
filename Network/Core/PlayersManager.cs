using COSML;
using COSML.Log;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace COSMP.Network.Core
{
    internal class PlayersManager : MonoBehaviour
    {
        private Dictionary<PlayerData, PlayerController> controllers;

        internal bool Exists(PlayerData data) => controllers.ContainsKey(data);

        internal void Start()
        {
            controllers = [];

            On.PlayerCollisionTrigger.OnTriggerStay += OnPlayerCollisionTriggerOnTriggerStay;
        }

        internal void OnDestroy()
        {
            On.PlayerCollisionTrigger.OnTriggerStay -= OnPlayerCollisionTriggerOnTriggerStay;


            PauseController pauseController = GameController.GetInstance().GetPauseController();
            Dictionary<NavMeshAgent, Vector3> navMeshAgentToResume = ReflectionHelper.GetField<PauseController, Dictionary<NavMeshAgent, Vector3>>(pauseController, "navMeshAgentToResume");
            Dictionary<Animator, float> animatorToResume = ReflectionHelper.GetField<PauseController, Dictionary<Animator, float>>(pauseController, "animatorToResume");
            foreach (PlayerController controller in controllers.Values)
            {
                try
                {
                    navMeshAgentToResume.Remove(controller.GetComponent<NavMeshAgent>());
                    animatorToResume.Remove(controller.GetComponent<Animator>());
                    animatorToResume.Remove(controller.transform.Find("MainPlayer/Char_Player").GetComponent<Animator>());
                    Destroy(controller.gameObject);
                }
                catch (Exception ex)
                {
                    Logging.Debug($"Failed to remove player controller:\n{ex}");
                }
            }
        }

        private void OnPlayerCollisionTriggerOnTriggerStay(On.PlayerCollisionTrigger.orig_OnTriggerStay orig, PlayerCollisionTrigger self, Collider otherCollider)
        {
            if (!IsMainHumanoidMove(self.GetComponent<HumanoidMove>())) return;

            orig(self, otherCollider);
        }

        internal void Add(PlayerData data)
        {
            PortalLoader portalLoader = ReflectionHelper.GetField<TitleScreen, PortalLoader>(GameController.GetInstance().GetPlaceController().titleScreen, "portalLoader");
            Portal portal = portalLoader.SearchPortal();
            PlayerController controller = Instantiate(AssetBundleController.GetInstance().GetPlayerTemplate(), null);
            controller.name = $"COSMP-Player-{data.Username.Replace(" ", "_")}";
            controller.Init(portal);
            controller.GetHumanoid().walkOnPortal = false;
            controller.gameObject.AddComponent<PlayerCanvas>().Init(data.Username);

            controllers.Add(data, controller);
            controller.transform.localPosition = data.Position.Position;
            MovePlayer(data);
            LookPlayer(data);
        }

        internal void Remove(PlayerData data)
        {
            if (!controllers.ContainsKey(data)) return;

            Destroy(controllers[data].gameObject);
            controllers.Remove(data);
        }

        internal void MovePlayer(PlayerData data)
        {
            if (!IsValidPlayer(data)) return;

            Logging.Debug($"MovePlayer: {data.Position} {data.Position.Run}");
            PlayerController controller = controllers[data];
            if (!GameController.GetInstance().GetPauseController().InPause()) controller.transform.localPosition = data.Position.Position;

            HumanoidMove humanoidMove = controller.GetHumanoid();
            humanoidMove.ChangeSpot(new WalkSpot(data.Position.Destination, humanoidMove, data.Position.Run, true), false, true, true);
        }

        internal void LookPlayer(PlayerData data)
        {
            if (!IsValidPlayer(data)) return;

            Logging.Debug($"LookPlayer: {data.Look}");

            PlayerController controller = controllers[data];
            controller.transform.localPosition = data.Position.Position;
            controller.GetHumanoid().Stop(false, false, false);
            controller.GetHumanoid().LookAt(data.Look);
        }

        internal void ActionPlayer(PlayerData data)
        {
            if (!IsValidPlayer(data)) return;

            Logging.Debug($"ActionPlayer: {data.Action}");
            controllers[data].GetHumanoid().StartAction(data.Action, null, true);
        }

        internal void CanvasActionPlayer(PlayerData data, PlayerCanvasAction action)
        {
            if (!IsValidPlayer(data)) return;

            Logging.Debug($"CanvasActionPlayer: {action}");
            controllers[data].GetComponent<PlayerCanvas>().SetAction(action);
        }

        internal void Loop()
        {
            foreach (PlayerData data in controllers.Keys)
            {
                PlayerController controller = controllers[data];
                bool display = data.Position.Place == GetCurrentPlace();
                controller.DisplayPlayer(display);
                controller.GetComponent<PlayerCanvas>().Toggle(display);

                if (!controller.IsDisplayed()) return;

                controller.GetHumanoid().Loop(true, false, false, GameController.GetInstance().GetPlaceController().GetCurrentPlace().GetPlayerSpecificAnimation());
                controller.GetComponentInChildren<PlayerCanvas>().Loop();
            }
        }

        internal void PhysicLoop()
        {
            foreach (PlayerController controller in controllers.Values)
            {
                if (!controller.IsDisplayed()) return;

                controller.PhysicLoop();
            }
        }

        internal static bool IsMainPlayerController(PlayerController self)
        {
            PlayerController playerController = GameController.GetInstance().GetPlayerController();
            return playerController != null && playerController.GetHashCode() == self.GetHashCode();
        }
        internal static bool IsMainHumanoidMove(HumanoidMove self)
        {
            HumanoidMove humanoidMove = GameController.GetInstance().GetPlayerController()?.GetHumanoid();
            return humanoidMove != null && humanoidMove.GetHashCode() == self.GetHashCode();
        }
        internal static string GetCurrentPlace() => GameController.GetInstance().GetPlaceController().GetCurrentPlace()?.gameObject.scene.name;
        internal static bool IsInGame() => GameController.GetInstance().GetPlayerController() != null;
        internal bool IsValidPlayer(PlayerData data) => Exists(data) && data.Position.Place == GetCurrentPlace() && data.Position.Place != "A00_TitleScreen";
    }
}
