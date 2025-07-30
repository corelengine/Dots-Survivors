using UnityEngine;

public class CameraTargetSingleton : MonoBehaviour
{
        public static CameraTargetSingleton Instance;

        public void Awake()
        {
                if (Instance != null)
                {
                        Debug.LogWarning("More than one instance of CameraTargetSingleton found!", Instance);
                        Destroy(gameObject);
                        return;
                }
                
                Instance = this;
        }
}