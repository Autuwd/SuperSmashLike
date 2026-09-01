using UnityEngine;
using SuperSmashLike.Managers;

public class TestEndMatch : MonoBehaviour
{
    void Start()
    {
        Invoke("Fire", 2f);
    }
    void Fire()
    {
        GetComponent<MatchManager>().EndMatch(0); // Ä£Äâ P1(Ë÷Òý0) »ñÊ¤
    }
}