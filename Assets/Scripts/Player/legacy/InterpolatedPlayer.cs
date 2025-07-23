using System.Collections.Generic;
using Script.Utilities;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Player
{
    public class InterpolatedPlayer : NetworkBehaviour
    {
        //input
        [SerializeReference] private InputActionAsset playerActions;
        [SerializeField] private float moveSpeed = 10f;

        //Net general
        private NetworkTimer timer;
        private float ServerTickRate => NetworkManager.Singleton.NetworkTickSystem.TickRate;
        private const int bufferSizes = 1024;

        //Net, client
        private CircularBuffer<TransformState> _clientStateBuffer;
        private CircularBuffer<MoveInput> _clientInputBuffer;
        private TransformState _lastServerState;
        private TransformState _lastProcessedState;

        //Net, Server
        CircularBuffer<TransformState>
            _serverStateBuffer; //technically redundant but I'll keep it for safety at this point
        private Queue<MoveInput> serverInputQueue;
        private MoveInput lastServerInput;

        //Reconciliation
        private float reconciliationThreshold = 3f; //generous reconciliation threshold
        private float reconciliationCooldown = 1f; //prevent the reconciliation from happening every frame (this is the sign something is wrong)
        private int lastReconciledTick;
        private void Awake()
        {
            timer = new NetworkTimer(ServerTickRate);
            _clientStateBuffer = new CircularBuffer<TransformState>(bufferSizes);
            _clientInputBuffer = new CircularBuffer<MoveInput>(bufferSizes);
            //should not be required on non server, I'll keep it for safety
            _serverStateBuffer = new CircularBuffer<TransformState>(bufferSizes);
            serverInputQueue = new Queue<MoveInput>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            transform.position = GetPlayerSpawnPosition(OwnerClientId);
            _lastServerState = new TransformState()
            {
                Position = transform.position,

            };
            _lastProcessedState = _lastServerState;
            var bufferIndex = timer.CurrentTick;
            _clientStateBuffer[bufferIndex] = _lastServerState;
            _serverStateBuffer[bufferIndex] = _lastServerState;
            if (!IsOwner) return;
            playerActions.Enable();
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            if (!IsOwner) return;
            playerActions.Disable();
        }

        private void Update()
        {
            timer.Update(Time.deltaTime);
        }

        private void FixedUpdate()
        {
            while (timer.shouldTick())
            {
                HandleClientTick();
                HandleServerTick();
            }
        }

        private void HandleServerTick()
        {
            if (!IsServer) return;
            var serverTick = NetworkManager.Singleton.ServerTime.Tick;
            var bufferIndex = serverTick % bufferSizes;
            //note if the input was not continuous we would see issue with inputs potentially arriving out of order, should not be an issue for now
            while (serverInputQueue.Count > 0)
            {
                if (serverTick < serverInputQueue.Peek().Tick) break; 
                lastServerInput = serverInputQueue.Dequeue();
                
            }
            TransformState statePayload = ProcessMovement(lastServerInput);
            _serverStateBuffer[bufferIndex] = statePayload;

            if (bufferIndex == -1) return;
            SendToClientRpc(_serverStateBuffer[bufferIndex]);
        }

        

        [ClientRpc]
        private void SendToClientRpc(TransformState transformState)
        {
            if (!IsOwner) return; //could be replaced with a more targeted Rpc, but let's not trust the API again
            _lastServerState = transformState;
        }

        private void HandleClientTick()
        {
            if (!IsClient || !IsOwner) return;//should also run on non owning clients

            var currentTick = timer.CurrentTick;
            var bufferIndex = currentTick % bufferSizes;

            MoveInput inputPayload = new MoveInput()
            {
                Tick = currentTick,
                InputValue = playerActions["Player/Move"].ReadValue<Vector2>(),
            };
            _clientInputBuffer[bufferIndex] = inputPayload;
            SendCurrentInputToServerRpc(inputPayload);

            TransformState statePayload = ProcessMovement(inputPayload);
            _clientStateBuffer[bufferIndex] = statePayload;

            //Reconciliation methods
            HandleServerReconciliation();
        }

        private void HandleServerReconciliation()
        {
            if (!ShouldReconcile()) return;

            float positionError;
            int bufferIndex;
            TransformState rewindState = default;
            bufferIndex = _lastServerState.Tick % bufferSizes;
            if (bufferIndex - 1 < 0)
                return; //the whole thing will fail on the buffer looping back because of this, but it might be drowned by the frequency
            rewindState = IsHost ? _serverStateBuffer[bufferIndex - 1] : _lastServerState;
            positionError = Vector3.Distance(rewindState.Position, _clientStateBuffer[bufferIndex].Position);

            if (positionError > reconciliationThreshold)
            {
                ReconcileState(rewindState);
                lastReconciledTick = timer.CurrentTick;
            }
        }

        private void ReconcileState(TransformState rewindState)
        {
            transform.position = rewindState.Position;
            if (!rewindState.Equals(_lastServerState)) return; //probably a hack for some issue with the host
            _clientStateBuffer[rewindState.Tick] = rewindState;
            int tickToReplay = _lastServerState.Tick;
            while (tickToReplay > timer.CurrentTick)
            {
                int bufferIndex = tickToReplay % bufferSizes;
                TransformState statePayload = ProcessMovement(_clientInputBuffer[bufferIndex]); //that should be valid as it only affect this object
                _clientStateBuffer[bufferIndex] = statePayload;
                tickToReplay++;
            }
        }

        private bool ShouldReconcile()
        {
            bool isNewServerState = !_lastServerState.Equals(default);
            bool isLastStateUndefinedOrDifferent =
                _lastProcessedState.Equals(default) || !_lastProcessedState.Equals(_lastServerState);
            return isNewServerState && isLastStateUndefinedOrDifferent && timer.CurrentTick > (lastReconciledTick + reconciliationCooldown*ServerTickRate);
        }

        [ServerRpc]
        private void SendCurrentInputToServerRpc(MoveInput inputPayload)
        {
            serverInputQueue.Enqueue(inputPayload);
        }

        TransformState ProcessMovement(MoveInput input)
        {
            Move(input.InputValue);
            return new TransformState()
            {
                Tick = input.Tick,
                Position = transform.position,
                //Velocity = input.InputValue
                //could have other values here
            };
        }

        private void Move(Vector2 inputVector)
        {
            transform.position +=
                new Vector3(inputVector.x, 0, inputVector.y) *
                (moveSpeed * Time.fixedDeltaTime); //in our case fixed update and tick have the same frequency
        }

        //utilities
        private Vector3 GetPlayerSpawnPosition(ulong ownerClientId)
        {
            //hardcoding it for now, there's only the single scene. You'd have spawn points registering to a singleton in a real game
            switch (ownerClientId)
            {
                case 0: return new Vector3(-5f, 2f, 0f);
                case 1: return new Vector3(5f, 2f, 0f);
                default: return new Vector3(0f, 2f, 0f);
            }
        }
    }
}
