using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AutoRotate : MonoBehaviour
{
    public float speed = 2.0f;

    // Update is called once per frame
    void Update()
    {
        transform.localRotation = Quaternion.Euler(0, transform.localRotation.eulerAngles.y + speed * Time.deltaTime, 0);
    }
}
