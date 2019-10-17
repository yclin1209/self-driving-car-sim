using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using SocketIO;
using UnityStandardAssets.Vehicles.Car;
using System;
using System.Security.AccessControl;
using System.Threading;
using UnityEngine.SceneManagement;

public class CommandServer : MonoBehaviour
{
	public CarRemoteControl CarRemoteControl;
	public Camera FrontFacingCamera;
	private SocketIOComponent _socket;
	private CarController _carController;


	public UISystem ui;
	private Thread thread;
	static bool flag = false;
	LogitechGSDK.DIJOYSTATE2ENGINES rec;

	int currentPos = 0;
	int moveToPos = 0;
	bool moved = false;
	const int WHEEL_RANGE = 300;
	float modelAngle = 0;
	bool manualMode = false;


	// YC added
	void Awake () {
		
		ui = GameObject.Find("UISystem").GetComponent<UISystem> ();
		flag = LogitechGSDK.LogiSteeringInitialize (false);
		Debug.Log ("SteeringInit:" + flag);
		Thread.Sleep (500);

		//
		// move the wheel to generate a read event
		//
		if (LogitechGSDK.LogiUpdate () && LogitechGSDK.LogiIsConnected (0)) {
			LogitechGSDK.LogiPlayConstantForce (0, 50);
			Thread.Sleep (50);
		}



		// generate thread, run later.
		thread = new Thread (g29_control);
		thread.IsBackground = true;
	}
	// YC added

	void FixedUpdate()
	{
		if ( manualMode == true ) {
				CarRemoteControl.maunal ();

				ui.DriveStatus_Text.color = Color.red;
				ui.DriveStatus_Text.text = "Mode: Manual";
		}else
		{
				CarRemoteControl.model ();

				ui.DriveStatus_Text.color = Color.white;
				ui.DriveStatus_Text.text = "Mode: Autonomous";
		}

	}

	// YC added
	void Update ()
	{
		//ui.SetMPHValue(_carController.CurrentSpeed);
		//ui.SetAngleValue(_carController.CurrentSteerAngle);

		if (LogitechGSDK.LogiUpdate () && LogitechGSDK.LogiIsConnected (0)) {
			
			if (moved == false) {

				moved = true;

				thread.Start ();
			}
			if (LogitechGSDK.LogiButtonTriggered (0, 23)) {

				manualMode = !manualMode;

			}
			if (LogitechGSDK.LogiButtonTriggered (0, 24)) {
				Scene scene = SceneManager.GetActiveScene();
				SceneManager.LoadScene( scene.name );
			}
		}
	}
	// YC added

	int normalize(double val, double valmin, double valmax, double min, double max)
	{
		return Convert.ToInt16((((val - valmin) / (valmax - valmin)) * (max - min)) + min);
	}

	void moveToDegree(float deg)
	{
		deg = (deg / WHEEL_RANGE) * 100;

		moveToPos =  Convert.ToInt16(deg);

		while (true) {
			rec = LogitechGSDK.LogiGetStateUnity (0);
			currentPos = normalize (rec.lX, -32767, +32767, 0, 100);


			if (deg < currentPos) {
				LogitechGSDK.LogiPlayConstantForce (0, +40);
			} else {
				LogitechGSDK.LogiPlayConstantForce (0, -40);
			}

			rec = LogitechGSDK.LogiGetStateUnity (0);
			currentPos = normalize (rec.lX, -32767, +32767, 0, 100);

			int min = moveToPos - 1;
			int max = moveToPos + 1;

			if (currentPos >= min && currentPos <= max) {
				Debug.Log ("wheel at pos:" + currentPos);
				Debug.Log ("move complete, turn off force");

				LogitechGSDK.LogiPlayConstantForce (0, 0);
				LogitechGSDK.LogiStopConstantForce (0);
				break;
			}
		}
	}

	private void g29_control()
	{
		int count = 0;
		int loopCount = 0;
		System.Random crandom = new System.Random ();


		Debug.Log ("g29_control thread start");
	
		if (LogitechGSDK.LogiUpdate () && LogitechGSDK.LogiIsConnected (0)) {
			
			moveToDegree (WHEEL_RANGE / 2);	// init, centering

			Thread.Sleep (1000);// sleep 1 sec

			while (true) {

				if (loopCount++ > 20)
					break;

				count = Convert.ToInt16 (modelAngle);
				count = count * 6;

				if (manualMode == false) {
					moveToDegree ((WHEEL_RANGE / 2) + count);	
				}

				Thread.Sleep (80 + crandom.Next (1, 40));  // random sleep,TBC

			}
		}

	}
		

	// Use this for initialization
	void Start()
	{
		_socket = GameObject.Find("SocketIO").GetComponent<SocketIOComponent>();
		_socket.On("open", OnOpen);
		_socket.On("steer", OnSteer);
		_socket.On("manual", onManual);
		_carController = CarRemoteControl.GetComponent<CarController>();


	}
		

	void OnOpen(SocketIOEvent obj)
	{
		Debug.Log("Connection Open");
		EmitTelemetry(obj);
	}

	// 
	void onManual(SocketIOEvent obj)
	{
		EmitTelemetry (obj);

		//Debug.Log("OnManual");
	}

	void OnSteer(SocketIOEvent obj)
	{
		JSONObject jsonObject = obj.data;
		//    print(float.Parse(jsonObject.GetField("steering_angle").str));
		CarRemoteControl.SteeringAngle = float.Parse(jsonObject.GetField("steering_angle").str);
		CarRemoteControl.Acceleration = float.Parse(jsonObject.GetField("throttle").str);
		EmitTelemetry(obj);

		// set the modelAngle
		modelAngle = CarRemoteControl.SteeringAngle *25;
		//Debug.Log(modelAngle);

	}

	void EmitTelemetry(SocketIOEvent obj)
	{
		UnityMainThreadDispatcher.Instance().Enqueue(() =>
		{
			//print("Attempting to Send...");

			// send only if it's not being manually driven
			//if ((Input.GetKey(KeyCode.W)) || (Input.GetKey(KeyCode.S))) {
			if ( manualMode == true ) {
				_socket.Emit("telemetry", new JSONObject());

			}
			else {
				// Collect Data from the Car
				Dictionary<string, string> data = new Dictionary<string, string>();
				data["steering_angle"] = _carController.CurrentSteerAngle.ToString("N4");
				data["throttle"] = _carController.AccelInput.ToString("N4");
				data["speed"] = _carController.CurrentSpeed.ToString("N4");
				data["image"] = Convert.ToBase64String(CameraHelper.CaptureFrame(FrontFacingCamera));
				_socket.Emit("telemetry", new JSONObject(data));

			}
		});
				
	}

	void OnDestroy()
	{
		OnApplicationQuit ();
	}

	void OnApplicationQuit()
	{
		thread.Abort ();
		LogitechGSDK.LogiStopConstantForce (0);
		Debug.Log("SteeringShutdown:" + LogitechGSDK.LogiSteeringShutdown());
	}


}