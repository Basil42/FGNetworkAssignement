using System;
using Network;
using Script.Utilities;
using UI;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

namespace Player
{
    public struct MoveInput : INetworkSerializable
    {
        public int Tick;
        public Vector2 InputValue;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Tick);
            serializer.SerializeValue(ref InputValue);
        }
    }

    public struct TransformState : INetworkSerializable, IEquatable<TransformState>
    {
        public int Tick;

        public Vector3 Position;

        //public Quaternion rotation; //leaving it out for now
        //public Vector3 Velocity; //might leave it out of this implementation

        //angular vel could also be there
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Tick);
            serializer.SerializeValue(ref Position);
        }

        public bool Equals(TransformState other)
        {
            return Tick == other.Tick && Position.Equals(other.Position);
        }

        public override bool Equals(object obj)
        {
            return obj is TransformState other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Tick, Position);
        }
    }

    public class PredictedPlayer : NetworkBehaviour
    {
        //input
        [SerializeReference] private InputActionAsset playerActions;
        [SerializeField] private float moveSpeed = 10f;

        private MoveInput _lastServerInput; //also useful to discard movement input that arrived late 
        private Vector2 _movementInputValue;
        private const float InputRefreshThreshold = 0.05f;
        
        //projectile 
        [SerializeField] private PredictedProjectile projectileRepresentationPrefab;
        
        //Constant
        private const float GameplayHeight = 2f;//y plane on which gameplay happens

        //network constants;
        private const int BufferSizes = 1024;

        //Net, client
        private CircularBuffer<TransformState> _clientStateBuffer; //kept for reconciliation
        private CircularBuffer<MoveInput> _clientInputBuffer; //kept for reconciliation, could be a lot sparser
        private TransformState _lastServerState;
        private Vector3 _errorToServer = Vector3.zero; //aggregate of measured and predicted error to the Server.

        //how much the movement can be dampened/boosted to correct error (it will be clamped to not overshoot)
        [Header("Prediction settings")] 
        //[SerializeField] private float minErrorCorrectionStrength = 0.2f;
        [SerializeField] private float maxErrorCorrectionStrength = 0.8f;//used proportionally to movement speed
        [SerializeField] private float errorCorrectionThreshold = 0.005f;
        

        private CircularBuffer<Vector3> _clientErrorBuffer;//also have to save error to recalculate correctly on server update
        private CircularBuffer<Vector3> _errorCorrectionBuffer;
        private Vector3 _correctionSinceLastTick=Vector3.zero;
        

        //debugging
        [FormerlySerializedAs("ServerDummy")] [SerializeField]
        private Transform serverDummy;

        private int _previousUpdatedTick;
        private NetworkVariable<int> _health = new NetworkVariable<int>(3);
        private HealthWidget _healthWidget;


        private void Awake()
        {
            //timer = new NetworkTimer(ServerTickRate);
            //could probably get rid of them on the server
            _clientStateBuffer = new CircularBuffer<TransformState>(BufferSizes);
            _clientInputBuffer = new CircularBuffer<MoveInput>(BufferSizes);
            _clientErrorBuffer = new CircularBuffer<Vector3>(BufferSizes);
            _errorCorrectionBuffer = new CircularBuffer<Vector3>(BufferSizes);
            serverDummy.transform.parent = null;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            AssignUIWidgets();
            transform.position = GetPlayerSpawnPosition(OwnerClientId);
            //seeding the system with valid data
            _lastServerState = new TransformState()
            {
                Position = transform.position,
            };
            if (IsOwner)
            {
                NetworkManager.Singleton.NetworkTickSystem.Tick += OnTickOwner;
                playerActions.Enable();
                playerActions["Player/Attack"].performed += OnAttackInput;
            }
            if (IsServer) NetworkManager.Singleton.NetworkTickSystem.Tick += OnTickServer;
            if (IsClient) NetworkManager.Singleton.NetworkTickSystem.Tick += OnTickClient;

            
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            _healthWidget.SetHealth(0);
            _healthWidget.StartRespawnTimer(SpawnManager.Instance.RespawnDelay);
            ReleaseUIWidgets();
            if (IsServer) NetworkManager.Singleton.NetworkTickSystem.Tick -= OnTickServer;
            if (IsClient) NetworkManager.Singleton.NetworkTickSystem.Tick -= OnTickClient;
            if (!IsOwner) return;
            NetworkManager.Singleton.NetworkTickSystem.Tick -= OnTickOwner;
            playerActions.Disable();
        }

        private void OnAttackInput(InputAction.CallbackContext obj)
        {
            Vector2 mouseScreenPosition = Mouse.current.position.value;
            float distanceToCamera = Camera.main!.transform.position.y - GameplayHeight;//assumption that there would always be a camera as there is no reason to receive input otherwise
            var targetPosition = Camera.main.ScreenToWorldPoint(new Vector3(mouseScreenPosition.x, mouseScreenPosition.y, distanceToCamera));
            Assert.AreApproximatelyEqual(GameplayHeight, targetPosition.y, 0.1f);
            ProjectileSpawnServerRpc(targetPosition);
            //if (!IsServer) SpawnProjectileRepresentation();
        }

        [ServerRpc(RequireOwnership = true)]
        private void ProjectileSpawnServerRpc(Vector3 targetPosition)
        {
            var instance = Instantiate(projectileRepresentationPrefab);
            instance.transform.position = transform.position;
            instance.Init(targetPosition - transform.position, NetworkObject.OwnerClientId);
            instance.GetComponent<NetworkObject>().Spawn();
        }


        

        private void Update()
        {
            var inputVector = _movementInputValue * (moveSpeed * Time.deltaTime);
            var inputMovement = new Vector3(inputVector.x, 0f, inputVector.y);
            transform.position += inputMovement;
            //Error smoothing
            if (IsServer) return; //no need to do this on the server
            serverDummy.position = transform.position - _errorToServer;
            if (_errorToServer.sqrMagnitude < errorCorrectionThreshold) return;
            var movementAlignedCorrection = Vector3.ClampMagnitude(_errorToServer, maxErrorCorrectionStrength * moveSpeed * Time.deltaTime);// movement oriented version: Vector3.ClampMagnitude(Vector3.Project(_errorToServer, inputMovement),maxErrorCorrectionStrength) * inputMovement.magnitude; 
            
            transform.position -= movementAlignedCorrection;
            _errorToServer -= movementAlignedCorrection;
            _correctionSinceLastTick += movementAlignedCorrection;
            
        }
        private void OnTickServer()
        {
            _lastServerState = new TransformState()
            {
                Position = transform.position,
                Tick = NetworkManager.Singleton.LocalTime.Tick,
            };
        }

        private void OnTickClient()
        {
            int tick = NetworkManager.Singleton.LocalTime.Tick;
            int bufferIndex = tick % BufferSizes;
            _clientStateBuffer[bufferIndex] = new TransformState()
            {
                Position = transform.position,
                Tick = tick,
            };
            _clientErrorBuffer[bufferIndex] = _errorToServer;
            _errorCorrectionBuffer[bufferIndex] = _correctionSinceLastTick;
            _correctionSinceLastTick = Vector3.zero;
        }
        private void OnTickOwner()
        {
            if (!IsOwner) return;
            var previousMovementInput = _movementInputValue;
            MoveInput newMovementInput = new()
            {
                InputValue = playerActions["Player/Move"].ReadValue<Vector2>(),
                Tick = NetworkManager.Singleton.LocalTime.Tick,
            };
            if ((previousMovementInput - newMovementInput.InputValue).sqrMagnitude > InputRefreshThreshold)
            {
                MovementInputChangeServerRpc(newMovementInput);
                _movementInputValue = newMovementInput.InputValue;
            }

            int tick = NetworkManager.Singleton.LocalTime.Tick;
            int bufferIndex = tick % BufferSizes;
            if (bufferIndex == _previousUpdatedTick)
            {
                Debug.LogWarning("Processed same tick twice.");
            }

            if (bufferIndex != (_previousUpdatedTick + 1) % BufferSizes)
            {
                Debug.LogWarning("missed a tick");
            }

            _previousUpdatedTick = bufferIndex;
            _clientInputBuffer[bufferIndex] = newMovementInput;
        }
        

        [ServerRpc(RequireOwnership = true)]
        private void MovementInputChangeServerRpc(MoveInput newMovementInputValue)
        {
            // Debug.Log(
            //     $"Received movement input from {newMovementInputValue.Tick} at server tick{NetworkManager.ServerTime.Tick}, locally {NetworkManager.LocalTime.Tick}");
            _lastServerInput = newMovementInputValue;
            _movementInputValue = newMovementInputValue.InputValue;
            MovementInputChangeClientRpc(_lastServerInput, _lastServerState);
        }

        [ClientRpc]
        private void MovementInputChangeClientRpc(MoveInput lastServerInput,TransformState serverStateSnapshot)
        {
            if (!IsOwner && _lastServerInput.Tick < lastServerInput.Tick)//ignoring input arriving out of order
            {
                _movementInputValue = lastServerInput.InputValue;
                _lastServerInput = lastServerInput;
            }
            _lastServerInput = lastServerInput;
            //error estimates
            var bufferIndex = serverStateSnapshot.Tick % BufferSizes;
            var errorDelta = _clientStateBuffer[bufferIndex].Position - serverStateSnapshot.Position;//  + _clientErrorBuffer[bufferIndex] ;//incorrect use of the tick I think
            _errorToServer = errorDelta;

        }


        //utilities
        private Vector3 GetPlayerSpawnPosition(ulong ownerClientId)
        {
            //hardcoding it for now, there's only the single scene. You'd have spawn points registering to a singleton in a real game
            switch (ownerClientId)
            {
                case 0: return new Vector3(-5f, GameplayHeight, 0f);
                case 1: return new Vector3(5f, GameplayHeight, 0f);
                default: return new Vector3(0f, GameplayHeight, 0f);
            }
        }
        
        public void ReceiveDamage(int i)
        {
            Assert.IsTrue(IsServer);//should not be called on client, will do nothing if attempted
            _health.Value -= 1;
            Debug.Log($"Health set to {_health.Value}");
            if (_health.Value <= 0)
            {
                SpawnManager.Instance.RequestPlayerRespawn(OwnerClientId);
                _healthWidget.StartRespawnTimer(SpawnManager.Instance.RespawnDelay);
                NetworkObject.Despawn();
            }
            
        }

        private void UpdateHealthWidget(int previous, int current)
        {
            _healthWidget.SetHealth(current);
        }
        private void AssignUIWidgets()
        {
            if (UIPools.GetHealthWidgetFromPool(out _healthWidget))
            {
                _healthWidget.ownerId =  OwnerClientId;
                _healthWidget.SetMax(_health.Value);
                _healthWidget.SetHealth(_health.Value);
                _health.OnValueChanged += UpdateHealthWidget;
            }
        }

        private void ReleaseUIWidgets()
        {
            UIPools.ReturnHealthWidgetToPool(_healthWidget);
        }
    }
}