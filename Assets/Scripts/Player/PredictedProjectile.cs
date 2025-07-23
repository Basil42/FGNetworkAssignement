using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Assertions;

namespace Player
{
    public class PredictedProjectile : NetworkBehaviour
    {
        
        //fields
        Vector3 _velocity;
        [SerializeField] private float projectileSpeed = 15f;
        private int _playerLayer;//not possible to evaluate at compile time
        private ulong _ownerId;
        private Rigidbody _rigidbody;//local rigidbody used for movement and collision

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
        }

        public void Init(Vector3 direction, ulong ownerId)
        {
            if (IsSpawned && !IsServer) return;
            _velocity = direction.normalized * projectileSpeed;
            _playerLayer = LayerMask.NameToLayer("Player");
            _ownerId = ownerId;
            Debug.Log(_velocity);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if(IsServer)InitializeProjectileClientRpc(_velocity,_ownerId);
        }

        [ClientRpc]
        private void InitializeProjectileClientRpc(Vector3 velocity, ulong ownerClientId)
        {
            _velocity = velocity;
            _ownerId = ownerClientId;
        }

        private void FixedUpdate()
        {
           _rigidbody.MovePosition(transform.position + _velocity * Time.fixedDeltaTime);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!IsServer) return;//ignore it outside the server
            if (other.gameObject.layer != _playerLayer)
            {
                NetworkObject.Despawn();
                return;
            }
            var playerComponent = other.gameObject.GetComponent<PredictedPlayer>();
            Assert.IsNotNull(playerComponent);//has to be a player
            if (playerComponent.OwnerClientId == _ownerId) return;//ignoring the player that fired the projectile
            playerComponent.ReceiveDamage(1);
            NetworkObject.Despawn();
        }
        
    }
}