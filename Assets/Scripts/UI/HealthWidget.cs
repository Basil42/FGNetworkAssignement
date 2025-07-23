using System.Collections;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace UI
{
    public class HealthWidget : MonoBehaviour
    {
        public ulong ownerId;
        private int _maxHealthValue;
        private float _targetFill = 0.0f;
        private float _fillAnimationRate = 0.5f;
        private float _currentFill = 0.0f;//so it will play a filling animation in most cases
    
        private Image  _image;
        private TMP_Text _text;
        // Start is called once before the first execution of Update after the MonoBehaviour is created
        void Start()
        {
            _image = GetComponent<Image>();
            _text = GetComponentInChildren<TMP_Text>();
            _text.enabled = false;
            _image.fillAmount = _currentFill;
            
        }

        // Update is called once per frame
        void Update()
        {
            if (Mathf.Approximately(_currentFill, _targetFill)) return;
            _currentFill = Mathf.MoveTowards(_currentFill, _targetFill, _fillAnimationRate * Time.deltaTime);
            _image.fillAmount = _currentFill;
        }

        public void SetMax(int healthValue)
        {
            _maxHealthValue = healthValue;
        }

        public void SetHealth(int healthValue)
        {
            _targetFill = (float)healthValue / (float)_maxHealthValue;
        }

        private Coroutine SpawnTimerCoroutine;
        public void StartRespawnTimer(float timeSec)
        {
            if(SpawnTimerCoroutine != null) StopCoroutine(SpawnTimerCoroutine);
            SpawnTimerCoroutine = StartCoroutine(RespawnTimerRoutine(timeSec));
        }

        private IEnumerator RespawnTimerRoutine(float timeSec)
        {
            float remainingTime = timeSec;
            _text.enabled = true;
            _text.text = Mathf.Ceil(remainingTime).ToString(CultureInfo.InvariantCulture);
            while (remainingTime > 0)
            {
                yield return null;
                remainingTime -= Time.deltaTime;
                _text.text = Mathf.Ceil(remainingTime).ToString(CultureInfo.InvariantCulture);
            }
            _text.enabled = false;
            SpawnTimerCoroutine = null;
            Debug.Log("timer completed");
        }
    }
}
