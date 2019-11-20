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
	static bool initflag = false;
	static bool stuckflag = false;

	LogitechGSDK.DIJOYSTATE2ENGINES rec;
	LogitechGSDK.LogiControllerPropertiesData properties;
	int currentPos = 0;
	int moveToPos = 0;
	bool moved = false;
	const int WHEEL_RANGE = 360; // must same with G HUB's setting
	float modelAngle = 0;
	bool manualMode = false;
	static int loopCount = 0;

	Vector2 car_pos;

	// YC added
	void Awake () {
		
		ui = GameObject.Find("UISystem").GetComponent<UISystem> ();

		properties = new LogitechGSDK.LogiControllerPropertiesData ();
		properties.forceEnable = false;
		properties.overallGain = 100;
		properties.springGain = 100;
		properties.damperGain = 100;
		properties.defaultSpringEnabled = true;
		properties.defaultSpringGain = 100;
		properties.combinePedals = false;
		properties.wheelRange = WHEEL_RANGE;

		properties.gameSettingsEnabled = false;
		properties.allowGameSettings = false;

		LogitechGSDK.LogiSetPreferredControllerProperties (properties);

		initflag = LogitechGSDK.LogiSteeringInitialize (false);
		Debug.Log ("SteeringInit:" + initflag);
		Thread.Sleep (500);

		//
		// move the wheel to generate a read event
		//
		if (LogitechGSDK.LogiUpdate () && LogitechGSDK.LogiIsConnected (0)) {
			LogitechGSDK.LogiPlayConstantForce (0, 40);
			Thread.Sleep (20);
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


		car_pos.x = GameObject.Find ("Car").transform.position.x + 42 ;
		car_pos.y =  Math.Abs((GameObject.Find ("Car").transform.position.z + 150 - 235 ));

		//Debug.Log ("x:" + GameObject.Find ("Car").transform.position.x +",y:" + GameObject.Find ("Car").transform.position.z );

		if (Time.frameCount % 100 == 0) {
			if ( (stuckflag == false) && (Math.Round (_carController.CurrentSpeed) == 0)) {
				stuckflag = true;
				Debug.Log ("1st time car stucks, stuckflag set");
			}
			else if((stuckflag == true) && (Math.Round (_carController.CurrentSpeed) == 0)){
				Debug.Log ("2nd time car stucks, reload scene");
				loopCount = 0;
				stuckflag = false;
				Scene scene = SceneManager.GetActiveScene();
				SceneManager.LoadScene( scene.name );
				//Debug.Log ("GC");
				System.GC.Collect ();
			}
		}
		/*
		if (Time.frameCount % 600 == 0) {
			//Debug.Log ("GC");
			System.GC.Collect ();
		}*/

	}

	// YC added
	void Update ()
	{

		if (LogitechGSDK.LogiUpdate () && LogitechGSDK.LogiIsConnected (0)) {
			
			if (moved == false) {
				moved = true;
				thread.Start ();
			}

			// enter key : manual/auto mode switch
			if (LogitechGSDK.LogiButtonTriggered (0, 23)) {
				manualMode = !manualMode;
			}

			// ps key : reload scene
			if (LogitechGSDK.LogiButtonTriggered (0, 24)) {
				loopCount = 0;
				Scene scene = SceneManager.GetActiveScene();
				SceneManager.LoadScene( scene.name );
			}

			// option key : change scene
			if (LogitechGSDK.LogiButtonTriggered (0, 9)) {
				loopCount = 0;
				Scene scene = SceneManager.GetActiveScene();

				if(scene.name == "JungleTrackAutonomous")
					SceneManager.LoadScene( "LakeTrackautonomous" );
				else
					SceneManager.LoadScene( "JungleTrackAutonomous" );
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
	

		if (LogitechGSDK.LogiUpdate () && LogitechGSDK.LogiIsConnected (0)) {

			int min, max;

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


				min = moveToPos - 1;
				max = moveToPos + 1;

				if (currentPos >= min && currentPos <= max) {
					//Debug.Log ("wheel at pos:" + currentPos);
					//Debug.Log ("move complete, turn off force");

					LogitechGSDK.LogiPlayConstantForce (0, 0);
					//LogitechGSDK.LogiStopConstantForce (0);
					break;
				}			
			}
		}
	}

	void g29_control()
	{
		int rotationAngle = 0;
		int savedAngle = 0;
		const int thresholdAngle = 7;

		System.Random crandom = new System.Random ();
	
		rec = LogitechGSDK.LogiGetStateUnity (0);
			
		moveToDegree (WHEEL_RANGE / 2);	// init, centering

		Thread.Sleep (1000);// sleep 1 sec

		while (true) {

#if false
		if (loopCount++ > 10000)
		{
			if (LogitechGSDK.LogiUpdate () && LogitechGSDK.LogiIsConnected (0)) {
						LogitechGSDK.LogiStopConstantForce (0);
			}
			break;
		}
#endif

		rotationAngle = Convert.ToInt16 (modelAngle);

		//Debug.Log ("savedAngle = "+savedAngle);
		//Debug.Log ("rotationAngle = "+rotationAngle);

		if (Math.Abs (savedAngle - rotationAngle) < thresholdAngle) {
					
					//Debug.Log ("continue");
					Thread.Sleep (25 + crandom.Next (5, 25));  // random sleep,TBC
					continue;
		}

		savedAngle = rotationAngle;
		//rotationAngle = rotationAngle * 6;
		rotationAngle = rotationAngle * 2;

		if (manualMode == false) {
					moveToDegree ((WHEEL_RANGE / 2) + rotationAngle);	
		}

		Thread.Sleep (25 + crandom.Next (5, 25));  // random sleep,TBC

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

		//Debug.Log("car_location=" + float.Parse(jsonObject.GetField("car_location").str));

	}

	void EmitTelemetry(SocketIOEvent obj)
	{
		UnityMainThreadDispatcher.Instance().Enqueue(() =>
		{
			//print("Attempting to Send...");

			// send only if it's not being manually driven
			//if ((Input.GetKey(KeyCode.W)) || (Input.GetKey(KeyCode.S))) {
			if ( manualMode == true ) {
				//YC comment _socket.Emit("telemetry", new JSONObject());
				//YC added begin
					// Collect Data from the Car
					Dictionary<string, string> data = new Dictionary<string, string>();
					data["steering_angle"] = _carController.CurrentSteerAngle.ToString("N4");
					data["throttle"] = _carController.AccelInput.ToString("N4");
					data["speed"] = _carController.CurrentSpeed.ToString("N4");
					data["image"] = Convert.ToBase64String(CameraHelper.CaptureFrame(FrontFacingCamera));
					data["car_location"] = Convert.ToString( Math.Round(car_pos.x) + ","+ Math.Round(car_pos.y) ) ;
					_socket.Emit("telemetry", new JSONObject(data));
				//YC added end
			}
			else {
				// Collect Data from the Car
				Dictionary<string, string> data = new Dictionary<string, string>();
				data["steering_angle"] = _carController.CurrentSteerAngle.ToString("N4");
				data["throttle"] = _carController.AccelInput.ToString("N4");
				data["speed"] = _carController.CurrentSpeed.ToString("N4");
				data["image"] = Convert.ToBase64String(CameraHelper.CaptureFrame(FrontFacingCamera));
				data["car_location"] = Convert.ToString( Math.Round(car_pos.x) + ","+ Math.Round(car_pos.y) ) ;
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