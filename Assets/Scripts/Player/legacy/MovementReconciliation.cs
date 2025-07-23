#define LOG
using System;
using System.Collections.Generic;
using Script.Utilities;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Assertions;

namespace Script.Player
{
    [RequireComponent(typeof(MovementServerPrediction),
        typeof(Rigidbody2D), 
        typeof(PlayerMovement))]
    [DefaultExecutionOrder(1)]
    public class MovementReconciliation : NetworkBehaviour
    {
        [SerializeField] private float positionReconcileTolerance = 0.01f;
        [SerializeField] float velocityReconcileTolerance = 0.01f;

        private Rigidbody2D _rb;
        private PlayerMovement _movement;
        //private MovementServerPrediction _prediction;//unfortunate dependency
        
        private MovementStatePayload _lastServerStateReceived;
        [Header("Buffers")]
        [Header("States")]
        private const int StateBufferCapacity = 1024;
        private int _startOfStateHistoryIndex = 0;//buffer index of the oldest relevant state
        private int _mostRecentStateIndex = 0;
        [Header("Inputs")]
        private const int InputBufferDefaultCapacity = 128;
        private CircularBuffer<MovementStatePayload> _clientStateHistoryBuffer = new(StateBufferCapacity);
        private Queue<MoveInputPayload> _localInputQueue = new();//only used for syncing purposes
        private readonly List<MoveInputPayload> _clientInputBuffer = new(InputBufferDefaultCapacity);
        private List<MoveInputPayload> _reconciliationInputList;//TODO: get rid of this

        private MoveInputPayload latestInputRegistered;
        
#if UNITY_EDITOR
        private Transform ServerDummy;
#endif
        private void Awake()
        {
            //_prediction = GetComponent<MovementServerPrediction>();
            _movement = GetComponent<PlayerMovement>();
            _rb = GetComponent<Rigidbody2D>();
#if UNITY_EDITOR
            ServerDummy = transform.GetChild(0);
            ServerDummy.parent = null;
#endif
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsServer)
            {
                enabled = false;
                return;
            }

            NetworkManager.NetworkTickSystem.Tick += OnTick;
            //_tickSystem = NetworkManager.NetworkTickSystem;
            PlayerReconciliation.Register(ReconciliationInit, ReconciliationStep, ReconciliationInputCheck);
        }

        

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            if (!IsServer)
            {
                PlayerReconciliation.UnRegister(ReconciliationInit, ReconciliationStep, ReconciliationInputCheck);
                NetworkManager.NetworkTickSystem.Tick -= OnTick;
            }
            
        
        }
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        private bool _bufferFilled = false;//TODO remove this once confirmed stable
#endif
        private void OnTick()
        {
            while (_localInputQueue.Count > 0){
                _clientInputBuffer.Add(_localInputQueue.Dequeue());//at this point target velocity will already be applied to movement (unfortunate dependency)
                
            }
            if(_clientInputBuffer.Count>0)latestInputRegistered = _clientInputBuffer[^1];
            PhysicsStateSample();
        }
        private void PhysicsStateSample(bool preserveTick = false)
        {
            Assert.IsFalse(IsServer, "Player state reconciliation trying to run on Server");
            //reconciliation data
            _clientStateHistoryBuffer[++_mostRecentStateIndex] = new MovementStatePayload
            {
                Tick = preserveTick ? _clientStateHistoryBuffer[_mostRecentStateIndex].Tick:NetworkManager.LocalTime.Tick,
                Position = _rb.position,
                Velocity = _rb.linearVelocity,
                TargetVelocity = _movement.TargetHorizontalVelocity
            };

            if ((_mostRecentStateIndex - _startOfStateHistoryIndex) % StateBufferCapacity == 0)
            {
                //most recent data erased start of history
                _startOfStateHistoryIndex++;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (!_bufferFilled)
                {
                    _bufferFilled = true;
                    Debug.Log($"client state buffer full for client {NetworkObjectId}.");
                }
#endif

            }
        }
#if UNITY_EDITOR
        private List<Transform> _reconciliationDummies = new();
        private float preReconciliationTargetVelocity;
        


#endif
        private void ReconciliationInit(int tick)
        {
            #if UNITY_EDITOR
            preReconciliationTargetVelocity = _movement.TargetHorizontalVelocity;
            #endif
            var reconciliationStartStateIndex = GetLatestStateIndexAt(tick);
            _mostRecentStateIndex = reconciliationStartStateIndex;
            _movement.SetState(_clientStateHistoryBuffer[reconciliationStartStateIndex]);
            _reconciliationInputList = new List<MoveInputPayload>(_clientInputBuffer);
            if (_reconciliationInputList.Count > 0)
            {
                _movement.OnInput(_reconciliationInputList[0]);
                //_prediction.OnInput(_reconciliationInputList[0]);
                _reconciliationInputList.RemoveAt(0);
            }
#if UNITY_EDITOR
            foreach (var d in _reconciliationDummies)
            {
                Destroy(d.gameObject);
            }
            _reconciliationDummies.Clear();
#endif
        }

        // ReSharper disable Unity.PerformanceAnalysis
        private int GetLatestStateIndexAt(int tick)
        {
            if (_clientStateHistoryBuffer[_startOfStateHistoryIndex].Tick > tick)
            {
                Debug.LogError($"no available history to reconcile to on object {NetworkObjectId}");
                return _startOfStateHistoryIndex;
            }

            if (_clientStateHistoryBuffer[_mostRecentStateIndex].Tick < tick)
            {
                Debug.LogError("Trying to get state from a future tick");
                return _startOfStateHistoryIndex;
            }
            try
            {
                int index = _startOfStateHistoryIndex;
                while (_clientStateHistoryBuffer[index + 1].Tick <= tick) index++;
                return index;
            }
            catch (IndexOutOfRangeException e)
            {
                Debug.LogException(e);
                throw e;
            }
            
        }

        private void ReconciliationStep(float deltaTime, int tick)
        {
            if (_reconciliationInputList.Count > 0 && _reconciliationInputList[0].Tick >= tick)
            {
                _movement.OnInput(_reconciliationInputList[0]);
                //_prediction.OnInput(_reconciliationInputList[0]);
                _reconciliationInputList.RemoveAt(0);
            }
            _movement.ProcessMove(deltaTime);
            PhysicsStateSample();
            
#if UNITY_EDITOR
            if(IsOwner)_reconciliationDummies.Add(Instantiate(ServerDummy,transform.position,Quaternion.identity));
#endif
        }
        private void ReconciliationInputCheck()//processes input that might have been entered since the last rtt
        {
            #if UNITY_EDITOR
            if (!Mathf.Approximately(preReconciliationTargetVelocity, _movement.TargetHorizontalVelocity))
            {
                Debug.LogError("Reconciliation failed to land on the same input");
            }
            #endif
            while (_reconciliationInputList.Count > 0)
            {
                Debug.LogWarning($"leftover Input {_reconciliationInputList.Count}");
                _movement.OnInput(_reconciliationInputList[0]);
                OnInput(_reconciliationInputList[0]);
                //_prediction.OnInput(_reconciliationInputList[0]);
                _reconciliationInputList.RemoveAt(0);
            }

            Assert.IsFalse(_movement.TargetHorizontalVelocity * latestInputRegistered.Vector.x < 0f - float.Epsilon);

        }

        private void ApplyState(MovementStatePayload lastServerStateReceived)
        {
            transform.position = lastServerStateReceived.Position;
            _rb.linearVelocity = lastServerStateReceived.Velocity;
        }
        public void OnInput(MoveInputPayload input)
        {
            _localInputQueue.Enqueue(input);
            _clientStateHistoryBuffer[_mostRecentStateIndex] = _movement.GetMoveState();//keep the state up to date for the tick, hacky
        }
        
        internal void UpdateStateAndReconcile(MovementStatePayload movementStatePayload)
        {
            _lastServerStateReceived = movementStatePayload;
            #if UNITY_EDITOR
            ServerDummy.position = _lastServerStateReceived.Position;
            #endif
            //clean the input buffer from entries already processed by server at the time of state emission
            while(_clientInputBuffer.Count > 0
               &&_clientInputBuffer[0].Tick < movementStatePayload.Tick
               ) 
            {
                _clientInputBuffer.RemoveAt(0);//input was likely digested if not, a new message will arrive soon
            }
            while (_clientStateHistoryBuffer[_startOfStateHistoryIndex].Tick < _lastServerStateReceived.Tick)
            {
                _startOfStateHistoryIndex++;
                if((_mostRecentStateIndex - _startOfStateHistoryIndex) % StateBufferCapacity == 0){
#if LOG
                    Debug.LogError("player did not have data more recent than the server tick");
#endif
                    ApplyState(_lastServerStateReceived);//somehow didn't find any recent local data
                    return;
                }
            }
                
            if (NeedsReconcile())
            {
#if LOG
                Debug.Log($"reconciliation requested by {NetworkObjectId}");
#endif
                Reconcile();
            }
        }

        private bool NeedsReconcile()
        {
             //sanity check if we have no data to reconcile with, this can happen if messages arrive out of order
            if (_clientStateHistoryBuffer[_startOfStateHistoryIndex].Tick > _lastServerStateReceived.Tick)
            {
                #if LOG
                Debug.LogWarning($"No data to reconcile on {NetworkObjectId}");
                #endif
                ApplyState(_lastServerStateReceived);//fallback
                return false;
            }
            //advancing in the state buffer until we get to the tick closest to the state sent by the server
            //Technically the closest could be a few tick before, as the physics system ticks faster than the network
            while (_clientStateHistoryBuffer[_startOfStateHistoryIndex].Tick < _lastServerStateReceived.Tick)
            {
                _startOfStateHistoryIndex++;
                if((_mostRecentStateIndex - _startOfStateHistoryIndex) % StateBufferCapacity == 0){
                    #if LOG
                    Debug.LogError("player did not have data more recent than the server tick");
                    #endif
                    ApplyState(_lastServerStateReceived);//somehow didn't find any recent local data
                    return false;
                }
            }
            //looping back the index if needed, to avoid potential int overflow
            _startOfStateHistoryIndex %= StateBufferCapacity;
            
            var stateToReconcile = _clientStateHistoryBuffer[_startOfStateHistoryIndex];
            _clientStateHistoryBuffer[_startOfStateHistoryIndex] = _lastServerStateReceived;
            #if UNITY_EDITOR
            if(!Mathf.Approximately(stateToReconcile.TargetVelocity, _lastServerStateReceived.TargetVelocity))
               Debug.LogWarning("mismatched input prediction");
            #endif
            var positionDrift = (stateToReconcile.Position - _lastServerStateReceived.Position).magnitude;
            var velocityDrift = (stateToReconcile.Velocity - _lastServerStateReceived.Velocity).magnitude;
            if (positionDrift > positionReconcileTolerance
                || velocityDrift > velocityReconcileTolerance)
            {
                Debug.Log($"position drift {positionDrift}" +
                          $"\n velocity drift {velocityDrift}");
                return true;
            }
                
            

            return false;
        }

        private void Reconcile()
        {
            PlayerReconciliation.Reconciliate(_lastServerStateReceived.Tick);
        }

        
    }
}