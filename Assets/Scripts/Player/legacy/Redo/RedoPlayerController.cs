using System;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

namespace Script.Player.Redo
{
    //I'll do movement in the same script here
    public class RedoPlayerController : NetworkBehaviour
    {
        [SerializeReference] private InputActionAsset playerActionAsset;
        private Rigidbody2D _rb;
        private float _targetHorizontalVelocity = 0f;
        [SerializeField] private float moveSpeed = 10.0f;
        [SerializeField] private float accelerationRate = 10f;

        private List<MoveInput> _localInputs = new();//timestamp/desired velocity

        private AnticipatedNetworkTransform _anticipatedTransform;
        private Vector3 _AnticipationDelta;
        [SerializeField] private float smoothTime = 0.1f;
        [SerializeField] private float smoothDistance = 0.2f;

        // Start is called before the first frame update
        private void Awake()
        {
            _rb =  GetComponent<Rigidbody2D>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _anticipatedTransform = GetComponent<AnticipatedNetworkTransform>();
            _anticipatedTransform.StaleDataHandling = StaleDataHandling.Ignore;
            if(IsOwner)BindToInput();
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            if (IsOwner) UnbindToInput();
        }

        public override void OnReanticipate(double lastRoundTripTime)
        {
            //recalculate here
            AnticipatedNetworkTransform.TransformState previousState = _anticipatedTransform.PreviousAnticipatedState;
            var localTime = NetworkManager.LocalTime.Time;
            var authorityTime = localTime - lastRoundTripTime;
            while (_localInputs.Count > 0 && _localInputs[0].Timestamp < authorityTime)
            {
                _targetHorizontalVelocity = _localInputs[0].DesiredHorizontalVelocity;
                _localInputs.RemoveAt(0);
            }

            int inputIndex = 0;
            while (authorityTime < NetworkManager.LocalTime.Time - Time.fixedDeltaTime)
            {
                //reanticipate until input / should ideally be also done in lockstep on every reanticipated transform
                MoveUpdate(Time.fixedDeltaTime);
                authorityTime += Time.fixedDeltaTime;
                if(_localInputs.Count > inputIndex && _localInputs[inputIndex].Timestamp < authorityTime)_targetHorizontalVelocity =  _localInputs[0].DesiredHorizontalVelocity;
            }

            _AnticipationDelta = previousState.Position - _anticipatedTransform.AnticipatedState.Position;
            if (smoothTime != 0f)
            {
                var deltaAnticipatedPositionSqr = Vector3.SqrMagnitude(previousState.Position - _anticipatedTransform.AnticipatedState.Position);
                if (deltaAnticipatedPositionSqr <= 0.05 * 0.05)
                {
                    _anticipatedTransform.AnticipateState(previousState);//small delta we just keep our previous prediction, no jitter
                }else if (deltaAnticipatedPositionSqr < smoothDistance * smoothDistance)
                {
                    _anticipatedTransform.Smooth(previousState,_anticipatedTransform.AnticipatedState,smoothTime);//some reconciliation
                }
                else
                {
                    Debug.Log("teleport, delta : "  + deltaAnticipatedPositionSqr);
                }
            }
            
        }

        private void OnMoveInput(InputAction.CallbackContext obj)
        {
            _targetHorizontalVelocity = obj.ReadValue<Vector2>().x * moveSpeed;//should probably make it a single axis
            
            //save the input in a buffer
            _localInputs.Add(new(NetworkManager.LocalTime.Time,_targetHorizontalVelocity));
			ServerOnMoveInputsRpc(_targetHorizontalVelocity);
        }
		
        [Rpc(SendTo.Server)]
		private void ServerOnMoveInputsRpc(float targetHorizontalSpeed)
		{
			_targetHorizontalVelocity = targetHorizontalSpeed;
		}

        private void FixedUpdate()
        {
            MoveUpdate(Time.fixedDeltaTime);
        }

        private void MoveUpdate(float dt)
        {
            var currentVelocity = _rb.linearVelocity;//might need to keep track of a properly anticipated speed
            _rb.linearVelocity = new Vector2(Mathf.MoveTowards(currentVelocity.x,_targetHorizontalVelocity, accelerationRate * dt), currentVelocity.y);
            var anticipatedTransformAnticipatedPos = _anticipatedTransform.AnticipatedState.Position;
            Vector3 positionDelta = _rb.linearVelocity * dt;
            _anticipatedTransform.AnticipateMove(anticipatedTransformAnticipatedPos + positionDelta);
        }

        private void OnGUI()
        {
            if (!IsOwner) return;
            GUILayout.Label(_AnticipationDelta.ToString());
        }

        //Owner only
        private void BindToInput()
        {
            playerActionAsset["Movement/Move"].performed += OnMoveInput;
            playerActionAsset.Enable();
        }
        private void UnbindToInput()
        {
            playerActionAsset["Movement/Move"].performed -= OnMoveInput;
        }

    }

    internal struct MoveInput
    {
        public double Timestamp;
        public float DesiredHorizontalVelocity;

        public MoveInput(double timestamp, float targetHorizontalVelocity)
        {
            Timestamp = timestamp;
            DesiredHorizontalVelocity = targetHorizontalVelocity;
        }
    }
}
