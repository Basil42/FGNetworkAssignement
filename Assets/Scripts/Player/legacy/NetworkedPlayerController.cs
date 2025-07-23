using Script.Utilities;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.InputSystem;
using static Unity.Networking.Transport.NetworkParameterConstants;

namespace Player
{
    public class NetworkedPlayerController : NetworkBehaviour
    {
        [SerializeReference] private InputActionAsset playerActions;
        
        [SerializeField] private float moveSpeed = 10.0f;
        //[SerializeField] private float accelerationRate = 10f;
        
        //input cache here, The input will be digested and reconciled per player (maybe also reconcile objects tagged as having been interacted with), a lot simpler than the full rollback.
        private uint _begin;//index 
        private uint _end;
        private CircularBuffer<MoveInputContinuous> _moveInputs;//size specified at runtime because the tick rate isn't defined at build time
        private CircularBuffer<Vector3> _positionSnapshots;//use to estimate how much we drift from the server
        private uint _mostRecentInputIndex;
        private MoveInputContinuous _currentMovementInput;

        private Vector3 estimatedDriftDelta;
        private Vector3 estimatedDrift;
        private float SmoothTimer;//Current state of the progressive reconcialiation
        private float SmoothingTime = 1.0f;//total duration of the smooth reconcialiation should it happen all the way

        private void Awake()
        {
            _mostRecentInputIndex = 0;
            _moveInputs =
                new CircularBuffer<MoveInputContinuous>(
                    (ConnectTimeoutMS / 1000) * (int)NetworkManager.NetworkTickSystem.TickRate);


        }

        
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            transform.position = GetPlayerSpawnPosition(OwnerClientId);
            _positionSnapshots = new CircularBuffer<Vector3>(_moveInputs.Size);
            if (!IsOwner) return;
            playerActions["Player/Move"].started += OnMoveInputContinuousClient;
            playerActions["Player/Move"].performed += OnMoveInputContinuousClient;
            playerActions["Player/Move"].canceled += OnMoveInputContinuousClient;
            playerActions.Enable();
            
        }


        

        //public override void OnReanticipate(double lastRoundTripTime)
        // {
        //     Assert.IsFalse(IsServer);
        //     var lastAnticipatedPosition = _anticipatedTransform.PreviousAnticipatedState.Position;
        //     //clean up stale inputs
        //     var inputStaleThreshold = NetworkManager.LocalTime.Time - lastRoundTripTime;
        //     while (_begin != _end && _moveInputs[_begin].Timestamp < inputStaleThreshold) _begin++;
        //     var reconciliatedInput = _begin == _end ? new MoveInputContinuous(0f, Vector2.zero) : _moveInputs[_begin];//setting first relevant input
        //
        //     var inputIndex = _begin +1;
        //     //replay 
        //     while (inputStaleThreshold < NetworkManager.LocalTime.Time)
        //     {
        //         ClosestAnticipatedPosition =  ClosestAnticipatedPosition + new Vector3(reconciliatedInput.InputValue.x,0f,reconciliatedInput.InputValue.y) * (moveSpeed * Time.fixedDeltaTime);
        //         inputStaleThreshold += Time.fixedDeltaTime;
        //         if (inputIndex != _end && _moveInputs[inputIndex].Timestamp >= inputStaleThreshold)
        //         {
        //             reconciliatedInput = _moveInputs[inputIndex++];
        //         }
        //     }
        //     ClosestAnticipatedPosition = _anticipatedTransform.AnticipatedState.Position + new Vector3(reconciliatedInput.InputValue.x,0f,reconciliatedInput.InputValue.y) * moveSpeed * Time.fixedDeltaTime;
        //     Debug.Log("Re-anticipated with an error of " +(lastAnticipatedPosition - ClosestAnticipatedPosition).ToString() + "delta was : " + lastRoundTripTime + "s");
        //     _anticipatedTransform.AnticipateMove(_anticipatedTransform.PreviousAnticipatedState.Position);
        //     SmoothTimer = 0f;
        //
        // }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            if (!IsOwner) return;
            playerActions["Player/Move"].started -= OnMoveInputContinuousClient;
            playerActions["Player/Move"].performed -= OnMoveInputContinuousClient;//this is incorrect for a value type I need to read it and manually detect changes
            playerActions["Player/Move"].canceled -= OnMoveInputContinuousClient;
            playerActions.Disable();
        }

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
        [ServerRpc]
        private void OnMoveInputContinuousServerRpc(MoveInputContinuous obj)
        {
            //immediate execution, send update to client via RCP;
            _currentMovementInput = obj;
            ConfirmMoveClientRpc(obj,transform.position);
            
        }
        private void OnMoveInputContinuousClient(InputAction.CallbackContext obj)
        {
            _currentMovementInput = new MoveInputContinuous(NetworkManager.LocalTime.Time, obj.ReadValue<Vector2>());
            _positionSnapshots[_mostRecentInputIndex] = transform.position;//might want to replace that by a predictive value instead of a rendered value
            _moveInputs[_mostRecentInputIndex++] = _currentMovementInput;
            _end++;
            OnMoveInputContinuousServerRpc(_currentMovementInput);
        }
        // private void OnMoveInputContinuousClientCancelled(InputAction.CallbackContext obj)
        // {
        //     //this is a work around a the unity input system resetting at a non zero value without input  
        //     _currentMovementInput = new MoveInputContinuous(NetworkManager.LocalTime.Time, Vector2.zero);
        //     _moveInputs[++_mostRecentInputIndex] = _currentMovementInput;
        //     OnMoveInputContinuousServerRpc(_currentMovementInput);
        // }

        [ClientRpc]
        private void ConfirmMoveClientRpc(MoveInputContinuous obj, Vector3 position)
        {
            if (IsOwner)
            {
                //confirmation and check if we're in need of reconcialiation
                Assert.AreEqual(obj.Timestamp, _moveInputs[_begin].Timestamp);//for testing, there are rare real world circumstances were input could be out of order (the server should discard inputs that are late
                estimatedDrift += position - _positionSnapshots[_begin];
                Debug.Log(estimatedDrift);//should be 0 on the first input
                _begin++;
                SmoothTimer = 0f;
                estimatedDriftDelta = estimatedDrift * Time.fixedDeltaTime / SmoothingTime;
            }
            else
            {
                //probably needs reconciliation at every input
            }
            //_currentMovementInput = obj;
            //might be important to keep input on non owning clients as well
            
        }
        

        

        private void FixedUpdate()
        {
            ProcessMovement();

        }

        private void ProcessMovement()
        {
            
            transform.position += (new Vector3(_currentMovementInput.InputValue.x,0f,_currentMovementInput.InputValue.y) * (moveSpeed * Time.fixedDeltaTime)) + (SmoothTimer < SmoothingTime ? estimatedDriftDelta : Vector3.zero);
            SmoothTimer+= Time.fixedDeltaTime;
            estimatedDrift -= estimatedDriftDelta;
        }

        private void OnGUI()
        {
            
            GUILayout.Label(playerActions["Player/Move"].ReadValue<Vector2>().ToString());
        }
    }
}
