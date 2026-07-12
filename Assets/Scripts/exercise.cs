using System.Collections;
using UnityEngine;

public class exercise : MonoBehaviour
{
    public int a;
    public BoxCollider boxcollider;
    IEnumerator asdf()
    {
        yield return new WaitForSeconds(1f);
    }
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        a=1;
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
