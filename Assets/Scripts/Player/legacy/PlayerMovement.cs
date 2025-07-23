using System.Globalization;
using Unity.Netcode;
using UnityEngine;

namespace Script.Player
{

    [RequireComponent(typeof(Rigidbody2D))]
    [DefaultExecutionOrder(0)]
    public class PlayerMovement : MonoBehaviour
    {
        private Rigidbody2D _rb;
        [SerializeField] private float moveSpeed = 10.0f;
        [SerializeField] private float accelerationRate = 10.0f;
        private float _targetHorizontalVelocity;

        
        //client data
        private MovementStatePayload _lastServerStateReceived;
        private MovementStatePayload _predictedServerState;
        
        private static int _estimatedServerTickDelay;

        public float TargetHorizontalVelocity => _targetHorizontalVelocity;

        #region Client and Server
        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
        }

        private void FixedUpdate()
        {
            ProcessMove(Time.fixedDeltaTime);
        }

        internal void ProcessMove(float deltaTime)
        {
            var currentVelocity = _rb.linearVelocity;
            _rb.linearVelocity = new Vector2(Mathf.MoveTowards(currentVelocity.x, _targetHorizontalVelocity, accelerationRate*deltaTime),
                currentVelocity.y);
        }
        private void UpdateMovement(Vector2 inputVector)
        {
            _targetHorizontalVelocity = inputVector.x * moveSpeed;
        }

        #endregion

        #region OwnerClientOnly

        private int _inputEmissionTick;
        private int _predictedProcessingTick;
        public void OnInput(MoveInputPayload payload)
        {
            UpdateMovement(payload.Vector);
        }
        #endregion

        internal MovementStatePayload GetMoveState()
        {
            return new MovementStatePayload(){
            Position = _rb.position,
            TargetVelocity = _targetHorizontalVelocity,
            Tick = NetworkManager.Singleton.LocalTime.Tick,
            Velocity = _rb.linearVelocity
            };
        }
        
        internal void SetState(MovementStatePayload reconciliationStartState)
        {
            _rb.position = reconciliationStartState.Position;
            _targetHorizontalVelocity = reconciliationStartState.TargetVelocity;
            _rb.linearVelocity = reconciliationStartState.Velocity;
        }
    }

    

    internal struct MovementStatePayload : INetworkSerializable
    {
        public int Tick;
        public Vector2 Position;
        public Vector2 Velocity;
        public float TargetVelocity;//could just be -1,0,1

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Tick);
            serializer.SerializeValue(ref Position);
            serializer.SerializeValue(ref Velocity);
            serializer.SerializeValue(ref TargetVelocity);
        }
    }

    public struct MoveInputPayload : INetworkSerializable
    {
        public Vector2 Vector;
        public int Tick;
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Vector);
            serializer.SerializeValue(ref Tick);
        }
    }
}