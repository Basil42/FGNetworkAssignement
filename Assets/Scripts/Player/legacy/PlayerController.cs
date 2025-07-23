using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.InputSystem;
using Vector2 = UnityEngine.Vector2;

namespace Script.Player
{
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(PlayerMovement))]
    public class PlayerController : NetworkBehaviour
    {
        [SerializeReference]InputActionAsset playerInputActions;
        private NetworkTickSystem _tickSystem;
        private Rigidbody2D _rb;
        private PlayerMovement _movement;
        [CanBeNull] private MovementReconciliation _reconciliation;
        [CanBeNull] private MovementServerPrediction _prediction;
        private readonly Queue<Vector2> _inputBuffer = new();

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _movement = GetComponent<PlayerMovement>();
            _prediction = GetComponent<MovementServerPrediction>();
            _reconciliation = GetComponent<MovementReconciliation>();
            Assert.IsNotNull(_movement);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsOwner) BindToInput();
            _tickSystem = NetworkManager.NetworkTickSystem;
            _tickSystem.Tick += ProcessInput;
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            _tickSystem.Tick -= ProcessInput;
        }

        private void OnEnable()
        {
            if (IsOwner) BindToInput();//if the player gets enabled after being spawned
        }

        private void OnDisable()
        {
            if (IsOwner) UnbindToInput();
        }

        private void UnbindToInput()
        {
            playerInputActions["Movement/Move"].performed -= OnMoveInput;
            Debug.Log("unbound input");

        }
        //owner only
        private void BindToInput()
        {
            playerInputActions["Movement/Move"].performed += OnMoveInput;
            playerInputActions.Enable();
            Debug.Log("bound input");

        }
        private void OnMoveInput(InputAction.CallbackContext obj)
        {
            _inputBuffer.Enqueue(obj.ReadValue<Vector2>());
        }

        private void ProcessInput()
        {
            while (_inputBuffer.Count > 0)
            {
                var payload = new MoveInputPayload
                {
                    Vector = _inputBuffer.Dequeue(),
                    Tick = NetworkManager.LocalTime.Tick
                };
                _movement.OnInput(payload);
                _reconciliation?.OnInput(payload);
                MoveInputRpc(payload);
            }
            
        }

        [Rpc(SendTo.Server)]
        private void MoveInputRpc(MoveInputPayload payload)
        {
            StartCoroutine(MoveInputRpcRoutine(payload));
        }

        private IEnumerator MoveInputRpcRoutine(MoveInputPayload payload)
        {
            Assert.IsTrue(IsServer);
            if (payload.Tick > _tickSystem.LocalTime.Tick)
                yield return new WaitUntil(() => payload.Tick <= _tickSystem.LocalTime.Tick);
            
            _movement.OnInput(payload);
            // ReSharper disable once Unity.NoNullPropagation
            SendStateToClientRpc(//confirming input was received and sending latest state
                new MovementStatePayload
                {
                    Tick = _tickSystem.LocalTime.Tick,
                    Position = transform.position,
                    Velocity = _rb.linearVelocity,
                    TargetVelocity = _movement.TargetHorizontalVelocity
                });
            
        }

        #region Client only
        [Rpc(SendTo.NotServer)]
        [SuppressMessage("ReSharper", "Unity.NoNullPropagation")] //no need to reconciliate on host
        private void SendStateToClientRpc(MovementStatePayload movementStatePayload, RpcParams Params = default)
        {
            try
            {
                Assert.IsFalse(IsServer);
                if (!Params.Receive.SenderClientId.Equals(NetworkManager.ServerClientId))
                {
                    throw new UnauthorizedAccessException("Received a Server Rpc from a non server client");
                }
                _prediction?.OnConfirmServerState(movementStatePayload.Tick);
                _reconciliation?.UpdateStateAndReconcile(movementStatePayload);
            }
            catch (UnauthorizedAccessException e)
            {
                Console.WriteLine(e);
                //handled by doing nothing
            }
            catch (AssertionException)
            {
                //handled by doing nothing
            }
        }

        private void OnGUI()
        {
            GUILayout.Label((_tickSystem.LocalTime.Tick - _tickSystem.ServerTime.Tick).ToString());
        }

        #endregion
        private void OnCollisionEnter2D(Collision2D other)
        {
            
            if(IsServer)SendStateToClientRpc(new MovementStatePayload
            {
                Tick = _tickSystem.LocalTime.Tick,
                Position = _rb.position,
                Velocity = _rb.linearVelocity,
                TargetVelocity = _movement.TargetHorizontalVelocity
            });
        }
    }
}
