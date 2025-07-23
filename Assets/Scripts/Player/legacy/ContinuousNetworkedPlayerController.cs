using System;
using Script.Utilities;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.InputSystem;
using static Unity.Networking.Transport.NetworkParameterConstants;


namespace Player
{
    [RequireComponent(typeof(AnticipatedNetworkTransform))]
    public class ContinuousNetworkedPlayerController : NetworkBehaviour
    {
        [SerializeReference] private InputActionAsset playerActions;
        private AnticipatedNetworkTransform _anticipatedNetworkTransform;
        private uint _begin;//index 
        private uint _end;
        private CircularBuffer<MoveInputContinuous> _MoveInputContinuouss;//size specified at runtime because the tick rate isn't defined at build time
        private uint _mostRecentInputIndex;
        private MoveInputContinuous _currentMovementInput;
        private const float MoveSpeed = 10f;

        private void Awake()
        {
            _anticipatedNetworkTransform = GetComponent<AnticipatedNetworkTransform>();
            _anticipatedNetworkTransform.StaleDataHandling = StaleDataHandling.Reanticipate;
            _MoveInputContinuouss = new CircularBuffer<MoveInputContinuous>((ConnectTimeoutMS / 1000) * (int)NetworkManager.NetworkTickSystem.TickRate);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            transform.position = GetPlayerSpawnPosition(OwnerClientId);
            if (!IsOwner) return;
            playerActions["Player/Move"].started += OnMoveInputContinuousClient;
            playerActions["Player/Move"].performed += OnMoveInputContinuousClient;
            playerActions["Player/Move"].canceled += OnMoveInputContinuousClient;
            playerActions.Enable();
            
        }

        public override void OnReanticipate(double lastRoundTripTime)
        {
            Debug.Log(_anticipatedNetworkTransform.PreviousAnticipatedState.Position - _anticipatedNetworkTransform.AuthoritativeState.Position);
            _anticipatedNetworkTransform.Smooth(_anticipatedNetworkTransform.PreviousAnticipatedState,_anticipatedNetworkTransform.AuthoritativeState,0.1f);
        }


        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            if (!IsOwner) return;
            playerActions["Player/Move"].started -= OnMoveInputContinuousClient;
            playerActions["Player/Move"].performed -= OnMoveInputContinuousClient;//this is incorrect for a value type I need to read it and manually detect changes
            playerActions["Player/Move"].canceled -= OnMoveInputContinuousClient;
            playerActions.Disable();
        }
        //input
        private void OnMoveInputContinuousClient(InputAction.CallbackContext obj)
        {
            _currentMovementInput = new MoveInputContinuous(NetworkManager.LocalTime.Time, obj.ReadValue<Vector2>());
            //_positionSnapshots[_mostRecentInputIndex] = transform.position;//might want to replace that by a predictive value instead of a rendered value
            _MoveInputContinuouss[_mostRecentInputIndex++] = _currentMovementInput;
            _end++;
            OnMoveInputContinuousServerRpc(_currentMovementInput);
        }
        [ServerRpc]
        private void OnMoveInputContinuousServerRpc(MoveInputContinuous inputPayload)
        {
            _currentMovementInput = inputPayload;
            ConfirmMoveClientRpc(inputPayload,transform.position);
        }
        [ClientRpc]
        private void ConfirmMoveClientRpc(MoveInputContinuous inputPayload, Vector3 transformPosition)
        {
            if (IsOwner) return;
            _currentMovementInput = inputPayload;

        }

        private void FixedUpdate()
        {
            ProcessMovement();
        }

        private void ProcessMovement()
        {
            _anticipatedNetworkTransform.AnticipateMove(transform.position + new Vector3(_currentMovementInput.InputValue.x, 0f, _currentMovementInput.InputValue.y) * (MoveSpeed * Time.fixedDeltaTime));
        }

        //utilities
        private Vector3 GetPlayerSpawnPosition(ulong ownerClientId)
        {
            //hardcoding it for now, there's only the single scene. You'd have spawn points registering to a singleton in a real game
            switch (ownerClientId)
            {
                case 0:return new Vector3(-5f,2f,0f);
                case 1:return new Vector3(5f,2f,0f);
                default:return new Vector3(0f,2f,0f);
            }
        }
    }
    internal struct MoveInputContinuous : INetworkSerializeByMemcpy
    {
        public double Timestamp;
        public Vector2 InputValue;

        public MoveInputContinuous(double timestamp, Vector2 value)
        {
            Timestamp = timestamp;
            InputValue = value;
        }
    }
}
