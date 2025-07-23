using System;
using System.Collections.Generic;
using System.Diagnostics;
using JetBrains.Annotations;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Assertions;
using Debug = UnityEngine.Debug;

namespace Script
{
    internal static class PlayerReconciliation
    {
        private static NetworkManager netManager;
        public static void Reconciliate(int tick)
        {
            // ReSharper disable once Unity.NoNullCoalescing
            netManager ??= NetworkManager.Singleton;
            Assert.IsNotNull(netManager);
            var tickDeltaTime = netManager.LocalTime.FixedDeltaTime;
            foreach (var playerInit in _playersReconciliationInits)
            {
                //Set all player to the tick to conciliate
                playerInit?.Invoke(tick);
            }
            Physics2D.simulationMode = SimulationMode2D.Script;
            float totalDeltaTime = ((float)(netManager.LocalTime.Tick - tick)/netManager.NetworkTickSystem.TickRate);
            float remainingDeltaTime = totalDeltaTime;
            #if UNITY_EDITOR && LOG
            if (totalDeltaTime < 0f)
            {
                
                Debug.LogError("negative delta time reconciliation ???");
            }
            #endif
            float partialDeltaTime = totalDeltaTime % tickDeltaTime;
            Assert.IsTrue(partialDeltaTime < tickDeltaTime);
            foreach (var playersReconciliationStep in _playersReconciliationSteps)
            {
                playersReconciliationStep?.Invoke(partialDeltaTime,tick + Mathf.FloorToInt((totalDeltaTime - remainingDeltaTime)*netManager.NetworkTickSystem.TickRate));
            }
            Physics2D.Simulate(partialDeltaTime);
            remainingDeltaTime -= partialDeltaTime;
            while (remainingDeltaTime >= tickDeltaTime - float.Epsilon)
            {
                foreach (var playersReconciliationStep in _playersReconciliationSteps)
                {
                    playersReconciliationStep?.Invoke(1.0f/netManager.NetworkTickSystem.TickRate,tick + Mathf.FloorToInt((totalDeltaTime - remainingDeltaTime)*netManager.NetworkTickSystem.TickRate));
                }

                remainingDeltaTime -= tickDeltaTime;
                Physics2D.Simulate(tickDeltaTime);
            }

            Physics2D.simulationMode = SimulationMode2D.FixedUpdate;
            foreach (var check in _playerSanityCheck)
            {
                check?.Invoke();
            }
        }

        private static List<Action<float,int>> _playersReconciliationSteps = new();
        private static List<Action<int>> _playersReconciliationInits = new();
        private static List<Action> _playerSanityCheck = new();
        public static void Register([NotNull] Action<int> onReconcileStart, [NotNull] Action<float,int> reconciliationStep,Action sanityCheck)
        {
            if (onReconcileStart == null) throw new ArgumentNullException(nameof(onReconcileStart));
            if (reconciliationStep == null) throw new ArgumentNullException(nameof(reconciliationStep));
            _playersReconciliationInits.Add(onReconcileStart);
            _playersReconciliationSteps.Add(reconciliationStep);
            _playerSanityCheck.Add(sanityCheck);
        }

        public static void UnRegister(Action<int> reconciliationInit, Action<float, int> reconciliationStep, Action sanityCheck)//should not require nullchecks
        {
            _playersReconciliationInits.Remove(reconciliationInit);
            _playersReconciliationSteps.Remove(reconciliationStep);
            _playerSanityCheck.Remove(sanityCheck);
        }
    }
}