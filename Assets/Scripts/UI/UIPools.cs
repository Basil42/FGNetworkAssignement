using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace UI
{
    /*
     * Very simple implementation with no guardrail against misuse, consumers are responsible for configuring the widgets
     */
    public class UIPools : MonoBehaviour
    {
        [SerializeField]private List<HealthWidget> healthWidgetsAvailablePool;//might add respawn timer in there
        private static UIPools _instance;


        private void Awake()
        {
            _instance = this;
        }

        public static bool GetHealthWidgetFromPool(out HealthWidget healthWidget)
        {
            if (_instance.healthWidgetsAvailablePool.Count == 0)
            {
                healthWidget = null;
                return false;
            }
            healthWidget = _instance.healthWidgetsAvailablePool[0];
            _instance.healthWidgetsAvailablePool.RemoveAt(0);
            return true;
        }

        public static void ReturnHealthWidgetToPool(HealthWidget healthWidget)
        {
            _instance.healthWidgetsAvailablePool.Add(healthWidget);
        }
        
    }
}