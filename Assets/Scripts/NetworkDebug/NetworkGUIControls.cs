using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using UnityEngine.Serialization;

public class NetworkGUIControls : MonoBehaviour
{
    [SerializeField]private NetworkManager networkManager;
    private GUIStyle _buttonStyle;
    private void Awake()
    {
        if (networkManager == null) enabled = false;
        
        
    }
    private void OnGUI()
    {
        _buttonStyle ??= new GUIStyle(GUI.skin.button)//hate this
        {
            fontSize = 32,
            fixedWidth = 300,
            fixedHeight = 150
        };
        if (GUILayout.Button("Host",_buttonStyle))
            networkManager?.StartHost();
        if (GUILayout.Button(("Client"),_buttonStyle)) 
            networkManager?.StartClient();
    }
}
