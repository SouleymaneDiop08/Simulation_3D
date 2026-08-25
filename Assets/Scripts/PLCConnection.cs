using UnityEngine;
using WebSocketSharp;


public class PLCConnection : MonoBehaviour
{

    private WebSocket ws;


    void Start()
    {

        Debug.Log("Connexion PLC...");


        ws = new WebSocket(
            "ws://localhost:8080"
        );


        ws.OnOpen += (sender, e) =>
        {
            Debug.Log("Connecté au PLC");
        };


        ws.OnMessage += (sender, e) =>
        {
            Debug.Log(
                "MESSAGE PLC : " + e.Data
            );
        };


        ws.OnError += (sender, e) =>
        {
            Debug.LogError(
                "Erreur : " + e.Message
            );
        };


        ws.OnClose += (sender, e) =>
        {
            Debug.Log(
                "PLC déconnecté"
            );
        };


        ws.Connect();

    }


    void OnApplicationQuit()
    {
        if(ws != null)
        {
            ws.Close();
        }
    }
}