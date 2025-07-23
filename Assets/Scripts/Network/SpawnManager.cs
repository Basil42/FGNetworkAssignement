using System;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Serialization;

namespace Network
{
    public class SpawnManager : MonoBehaviour
    {
        [SerializeField]private NetworkObject playerPrefab;
        [field: SerializeField] public float RespawnDelay { get; } = 3f;
        public static SpawnManager Instance;

        private void Awake()
        {
            Instance =  this;
            Assert.IsNotNull(playerPrefab);
        }

        public async void RequestPlayerRespawn(ulong OwnerId)
        {
            try
            {
                Assert.IsTrue(NetworkManager.Singleton.IsServer); //should not do anything
                Debug.Log("Respawn requested");
                await Task.Delay(TimeSpan.FromSeconds(RespawnDelay));
                playerPrefab.InstantiateAndSpawn(NetworkManager.Singleton,OwnerId,true,true);

            }
            catch (Exception e)//safety to avoid crashes on void async
            {
                Debug.LogException(e);
            }
            
        }
    }
}