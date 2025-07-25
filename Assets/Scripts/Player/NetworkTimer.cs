﻿namespace Player
{
    //Used to replace the time and tick  system of NGO
    public class NetworkTimer
    {
        private float timer;
        public float MinTimeBetweenTicks { get; }
        public int CurrentTick { get; private set; }

        public NetworkTimer(float serverTickRate)
        {
            MinTimeBetweenTicks = 1f/serverTickRate;
        }

        public void Update(float deltaTime)
        {
            timer += deltaTime;
        }
        public bool shouldTick()
        {
            if (timer >= MinTimeBetweenTicks)
            {
                timer -= MinTimeBetweenTicks;
                CurrentTick++;
                return true;
            }
            return false;
        }
    }
}