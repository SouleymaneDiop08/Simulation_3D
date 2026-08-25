using UnityEngine;

public class TrackConnection : MonoBehaviour
{
    public TrackSystem voieArrivee;

    public float distanceEntree;


    public TrackSystem GetNextTrack()
    {
        return voieArrivee;
    }


    public float GetEntryDistance()
    {
        return distanceEntree;
    }
}