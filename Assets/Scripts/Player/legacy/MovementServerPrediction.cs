using System.Collections.Generic;
using Script.Utilities;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Assertions;

namespace Script.Player
{
    //class that delays inputs to be received by a simulated player
    public class MovementServerPrediction : NetworkBehaviour
    {
        private NetworkTickSystem _tickSystem;
        private PlayerMovement _serverPredictionBody;//a rigidbody on a separate layer to run movement on
        
        //buffer
        private readonly Queue<MoveInputPayload> _predictionInputQueue = new();//needed capacity should be fairly low but latency and player dependen
        private readonly List<MoveInputPayload> _predictionInputBuffer = new (10);
        
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _tickSystem = NetworkManager.NetworkTickSystem;
            if (IsServer)
            {
                enabled = false;
                return;
            }
            //Prediction object
            //Ideally you would create an adequate dummy prefab at build time
            _serverPredictionBody = new GameObject($"ServerPrediction{NetworkObjectId}", typeof(Rigidbody2D),
                typeof(BoxCollider2D),typeof(PlayerMovement)).GetComponent<PlayerMovement>();
            //Prediction objects only interact with each other and static geometry
            _serverPredictionBody.gameObject.layer = LayerMask.NameToLayer("Prediction");
            _serverPredictionBody.GetComponent<Rigidbody2D>().GetCopyOf(GetComponent<Rigidbody2D>());
            _serverPredictionBody.GetComponent<BoxCollider2D>().GetCopyOf(GetComponent<BoxCollider2D>());
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            if(IsServer)return;
            Destroy(_serverPredictionBody.gameObject);
        }

        private void Update()
        {
            Assert.IsFalse(IsServer);
            if (_predictionInputQueue.TryPeek(out MoveInputPayload predicted)
                && predicted.Tick >= _tickSystem.ServerTime.Tick)
            {
                var input = _predictionInputQueue.Dequeue();
                _serverPredictionBody.OnInput(input);
            }
        }

        public void OnInput(MoveInputPayload inputVector)
        {
            _predictionInputQueue.Enqueue(new MoveInputPayload
            {
                Vector = inputVector.Vector,
                Tick = _tickSystem.LocalTime.Tick
            });
            _predictionInputBuffer.Add(inputVector);
        }
        //clearing obsolete data from before the provided server tick
        internal void OnConfirmServerState(int tick)
        {
            while(_predictionInputBuffer.Count > 0
                  && _predictionInputBuffer[0].Tick < tick)_predictionInputBuffer.RemoveAt(0);
        }
        //rebuilding the queue at the stated local tick
        internal void ResetTo(int tick)
        {
            _predictionInputQueue.Clear();
            foreach (var input in _predictionInputBuffer)
            {
                if(input.Tick < tick)continue;
                _predictionInputQueue.Enqueue(input);
            }
        }

        // internal MovementStatePayload GetState()
        // {
        //     return _serverPredictionBody.GetMoveState();
        // }
    }
}