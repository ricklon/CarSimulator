using UnityEngine;
using System.Collections;

public class Car : MonoBehaviour {
public float wheelRadius = 0.4f;
float suspensionRange = 0.1f;
float suspensionDamper = 50;
float suspensionSpringFront = 18500;
float suspensionSpringRear = 9000;

public Material brakeLights;
public Vector3 dragMultiplier = new Vector3(2, 5, 1);

public float throttle = 0; 
float steer = 0;
bool  handbrake = false;

public Transform centerOfMass;

public Transform[] frontWheels;
public Transform[] rearWheels;

public Wheel[] wheels = null;

public WheelFrictionCurve wfc;

public float topSpeed = 160;
public int numberOfGears = 5;

public int maximumTurn = 15;
public int minimumTurn = 10;

public float resetTime = 5.0f;
float resetTimer = 0.0f;

float[] engineForceValues;
float[] gearSpeeds;

int currentGear;
float currentEnginePower = 0.0f;

float handbrakeXDragFactor = 0.5f;
float initialDragMultiplierX = 10.0f;
float handbrakeTime = 0.0f;
float handbrakeTimer = 1.0f;

Skidmarks skidmarks = null;
ParticleEmitter skidSmoke = null;
public float[] skidmarkTime;

//public SoundController sound;
bool  canSteer;
bool  canDrive;

public class Wheel
{
	public WheelCollider collider;
	public Transform wheelGraphic;
	public Transform tireGraphic;
	public bool  driveWheel = false;
	public bool  steerWheel = false;
	public int lastSkidmark = -1;
	public Vector3 lastEmitPosition = Vector3.zero;
	public float lastEmitTime = Time.time;
	public Vector3 wheelVelo = Vector3.zero;
	public Vector3 groundSpeed = Vector3.zero;
}

void  Start (){	
	wheels = new Wheel[frontWheels.Length + rearWheels.Length];
	// Measuring 1 - 60
	
	SetupWheelColliders();
	
	SetupCenterOfMass();
	
	topSpeed = Convert_Miles_Per_Hour_To_Meters_Per_Second(topSpeed);
	
	SetupGears();
	
	SetUpSkidmarks();
	
	initialDragMultiplierX = dragMultiplier.x;
}
//to test the car in manual mode uncomment this and comment ProcessAICommands
/*void  Update (){		
	Vector3 relativeVelocity = transform.InverseTransformDirection(rigidbody.velocity);
	
	GetInput();
	//RecalculateTrajectory();
	
	Check_If_Car_Is_Flipped();
	
	UpdateWheelGraphics(relativeVelocity);
	
	UpdateGear(relativeVelocity);
}*/

public void  ProcessAICommands (float throttle, float steer){
	this.throttle=throttle;
	this.steer=steer;
	Vector3 relativeVelocity = transform.InverseTransformDirection(rigidbody.velocity);
	UpdateWheelGraphics(relativeVelocity);
	UpdateGear(relativeVelocity);
}

void  FixedUpdate (){	
	// The rigidbody velocity is always given in world space, but in order to work in local space of the car model we need to transform it first.
	Vector3 relativeVelocity = transform.InverseTransformDirection(rigidbody.velocity);
	
	CalculateState();	
	
	UpdateFriction(relativeVelocity);
	
	UpdateDrag(relativeVelocity);
	
	CalculateEnginePower(relativeVelocity);
	
	ApplyThrottle(canDrive, relativeVelocity);
	
	ApplySteering(canSteer, relativeVelocity);
}

/**************************************************/
/* Functions called from Start()                  */
/**************************************************/

void  SetupWheelColliders (){
	SetupWheelFrictionCurve();
		
	int wheelCount = 0;
	
	foreach(Transform t in frontWheels)
	{
		wheels[wheelCount] = SetupWheel(t, true);
		wheelCount++;
	}
	
	foreach(Transform t in rearWheels)
	{
		wheels[wheelCount] = SetupWheel(t, false);
		wheelCount++;
	}
}

void  SetupWheelFrictionCurve (){
	wfc = new WheelFrictionCurve();
	wfc.extremumSlip = 1;
	wfc.extremumValue = 50;
	wfc.asymptoteSlip = 2;
	wfc.asymptoteValue = 25;
	wfc.stiffness = 1;
}

Wheel  SetupWheel ( Transform wheelTransform ,   bool isFrontWheel  ){
	 GameObject go = new GameObject(wheelTransform.name + " Collider");
	go.transform.position = wheelTransform.position;
	go.transform.parent = transform;
	go.transform.rotation = wheelTransform.rotation;
		
	WheelCollider wc = go.AddComponent(typeof(WheelCollider)) as WheelCollider;
	wc.suspensionDistance = suspensionRange;
	JointSpring js = wc.suspensionSpring;
	
	if (isFrontWheel)
		js.spring = suspensionSpringFront;
	else
		js.spring = suspensionSpringRear;
		
	js.damper = suspensionDamper;
	wc.suspensionSpring = js;
		
	Wheel wheel = new Wheel(); 
	wheel.collider = wc;
	wc.sidewaysFriction = wfc;
	wheel.wheelGraphic = wheelTransform;
	wheel.tireGraphic = wheelTransform.GetComponentsInChildren<Transform>()[1];
	
	wheelRadius = wheel.tireGraphic.renderer.bounds.size.y / 2;	
	wheel.collider.radius = wheelRadius;
	
	if (isFrontWheel)
	{
		wheel.steerWheel = true;
		
		go = new GameObject(wheelTransform.name + " Steer Column");
		go.transform.position = wheelTransform.position;
		go.transform.rotation = wheelTransform.rotation;
		go.transform.parent = transform;
		wheelTransform.parent = go.transform;
	}
	else
		wheel.driveWheel = true;
		
	return wheel;
}

void  SetupCenterOfMass (){
	if(centerOfMass != null)
		rigidbody.centerOfMass = centerOfMass.localPosition;
}

void  SetupGears (){
	engineForceValues = new float[numberOfGears];
	gearSpeeds = new float[numberOfGears];
	
	float tempTopSpeed = topSpeed;
		
	for(int i= 0; i < numberOfGears; i++)
	{
		if(i > 0)
			gearSpeeds[i] = tempTopSpeed / 4 + gearSpeeds[i-1];
		else
			gearSpeeds[i] = tempTopSpeed / 4;
		
		tempTopSpeed -= tempTopSpeed / 4;
	}
	
	float engineFactor = topSpeed / gearSpeeds[gearSpeeds.Length - 1];
	
	for(int i = 0; i < numberOfGears; i++)
	{
		float maxLinearDrag = gearSpeeds[i] * gearSpeeds[i];// * dragMultiplier.z;
		engineForceValues[i] = maxLinearDrag * engineFactor;
	}
}

void  SetUpSkidmarks (){
	if(FindObjectOfType(typeof(Skidmarks)))
	{
		skidmarks = (Skidmarks)FindObjectOfType(typeof(Skidmarks));
		skidSmoke = skidmarks.GetComponentInChildren<ParticleEmitter>();
	}
	else
		Debug.Log("No skidmarks object found. Skidmarks will not be drawn");
		
	skidmarkTime = new float[4];
	for(int i=0; i<4; i++){
		skidmarkTime[i] = 0.0f;
	}
}

/**************************************************/
/* Functions called from Update()                 */
/**************************************************/

void  GetInput (){
	throttle = Input.GetAxis("Vertical");
	steer = Input.GetAxis("Horizontal");
	
	if(throttle < 0.0f)
		brakeLights.SetFloat("_Intensity", Mathf.Abs(throttle));
	else
		brakeLights.SetFloat("_Intensity", 0.0f);
	
	CheckHandbrake();
}

void  CheckHandbrake (){
	if(Input.GetKey("space"))
	{
		if(!handbrake)
		{
			handbrake = true;
			handbrakeTime = Time.time;
			dragMultiplier.x = initialDragMultiplierX * handbrakeXDragFactor;
		}
	}
	else if(handbrake)
	{
		handbrake = false;
		StartCoroutine(StopHandbraking(Mathf.Min(5, Time.time - handbrakeTime)));
	}
}

IEnumerator  StopHandbraking ( float seconds  ){
	float diff = initialDragMultiplierX - dragMultiplier.x;
	handbrakeTimer = 1;
	
	// Get the x value of the dragMultiplier back to its initial value in the specified time.
	while(dragMultiplier.x < initialDragMultiplierX && !handbrake)
	{
		dragMultiplier.x += diff * (Time.deltaTime / seconds);
		handbrakeTimer -= Time.deltaTime / seconds;
		yield return 0;
	}
	
	dragMultiplier.x = initialDragMultiplierX;
	handbrakeTimer = 0;
}

void  Check_If_Car_Is_Flipped (){
	if(transform.localEulerAngles.z > 80 && transform.localEulerAngles.z < 280)
		resetTimer += Time.deltaTime;
	else
		resetTimer = 0;
	
	if(resetTimer > resetTime)
		FlipCar();
}

void  FlipCar (){
	transform.rotation = Quaternion.LookRotation(transform.forward);
	transform.position += Vector3.up * 0.5f;
	rigidbody.velocity = Vector3.zero;
	rigidbody.angularVelocity = Vector3.zero;
	resetTimer = 0;
	currentEnginePower = 0;
}

public int wheelCount;
void  UpdateWheelGraphics ( Vector3 relativeVelocity  ){
	wheelCount = -1;
	
	foreach(Wheel w in wheels)
	{
		wheelCount++;
		WheelCollider wheel = w.collider;
		WheelHit wh = new WheelHit();
		
		// First we get the velocity at the point where the wheel meets the ground, if the wheel is touching the ground
		if(wheel.GetGroundHit(out wh))
		{
			w.wheelGraphic.localPosition = wheel.transform.up * (wheelRadius + wheel.transform.InverseTransformPoint(wh.point).y);
			w.wheelVelo = rigidbody.GetPointVelocity(wh.point);
			w.groundSpeed = w.wheelGraphic.InverseTransformDirection(w.wheelVelo);
			
			// Code to handle skidmark drawing. Not covered in the tutorial
			if(skidmarks)
			{
				if(skidmarkTime[wheelCount] < 0.02f && w.lastSkidmark != -1)
				{
					skidmarkTime[wheelCount] += Time.deltaTime;
				}
				else
				{
					float dt = skidmarkTime[wheelCount] == 0.0f ? Time.deltaTime : skidmarkTime[wheelCount];
					skidmarkTime[wheelCount] = 0.0f;

					float handbrakeSkidding = handbrake && w.driveWheel ? w.wheelVelo.magnitude * 0.3f : 0;
					float skidGroundSpeed= Mathf.Abs(w.groundSpeed.x) - 15;
					if(skidGroundSpeed > 0 || handbrakeSkidding > 0)
					{
						Vector3 staticVel = transform.TransformDirection(skidSmoke.localVelocity) + skidSmoke.worldVelocity;
						if(w.lastSkidmark != -1)
						{
							float emission = UnityEngine.Random.Range(skidSmoke.minEmission, skidSmoke.maxEmission);
							float lastParticleCount = w.lastEmitTime * emission;
							float currentParticleCount = Time.time * emission;
							int noOfParticles = Mathf.CeilToInt(currentParticleCount) - Mathf.CeilToInt(lastParticleCount);
							int lastParticle = Mathf.CeilToInt(lastParticleCount);
							
							for(int i= 0; i <= noOfParticles; i++)
							{
								float particleTime = Mathf.InverseLerp(lastParticleCount, currentParticleCount, lastParticle + i);
								skidSmoke.Emit(	Vector3.Lerp(w.lastEmitPosition, wh.point, particleTime) + new Vector3(Random.Range(-0.1f, 0.1f), Random.Range(-0.1f, 0.1f), Random.Range(-0.1f, 0.1f)), staticVel + (w.wheelVelo * 0.05f), Random.Range(skidSmoke.minSize, skidSmoke.maxSize) * Mathf.Clamp(skidGroundSpeed * 0.1f,0.5f,1), Random.Range(skidSmoke.minEnergy, skidSmoke.maxEnergy), Color.white);
							}
						}
						else
						{
							skidSmoke.Emit(	wh.point + new Vector3(Random.Range(-0.1f, 0.1f), Random.Range(-0.1f, 0.1f), Random.Range(-0.1f, 0.1f)), staticVel + (w.wheelVelo * 0.05f), Random.Range(skidSmoke.minSize, skidSmoke.maxSize) * Mathf.Clamp(skidGroundSpeed * 0.1f,0.5f,1), Random.Range(skidSmoke.minEnergy, skidSmoke.maxEnergy), Color.white);
						}
					
						w.lastEmitPosition = wh.point;
						w.lastEmitTime = Time.time;
					
						w.lastSkidmark = skidmarks.AddSkidMark(wh.point + rigidbody.velocity * dt, wh.normal, (skidGroundSpeed * 0.1f + handbrakeSkidding) * Mathf.Clamp01(wh.force / wheel.suspensionSpring.spring), w.lastSkidmark);
//						sound.Skid(true, Mathf.Clamp01(skidGroundSpeed * 0.1f));
					}
					else
					{
						w.lastSkidmark = -1;
//						sound.Skid(false, 0);
					}
				}
			}
		}
		else
		{
			// If the wheel is not touching the ground we set the position of the wheel graphics to
			// the wheel's transform position + the range of the suspension.
			w.wheelGraphic.position = wheel.transform.position + (-wheel.transform.up * suspensionRange);
			if(w.steerWheel)
				w.wheelVelo *= 0.9f;
			else
				w.wheelVelo *= 0.9f * (1 - throttle);
			
			if(skidmarks)
			{
				w.lastSkidmark = -1;
//				sound.Skid(false, 0);
			}
		}
		// If the wheel is a steer wheel we apply two rotations:
		// *Rotation around the Steer Column (visualizes the steer direction)
		// *Rotation that visualizes the speed
		if(w.steerWheel)
		{
			Vector3 ea = w.wheelGraphic.parent.localEulerAngles;
			ea.y = steer * maximumTurn;
			w.wheelGraphic.parent.localEulerAngles = ea;
			w.tireGraphic.Rotate(Vector3.right * (w.groundSpeed.z / wheelRadius) * Time.deltaTime * Mathf.Rad2Deg);
		}
		else if(!handbrake && w.driveWheel)
		{
			// If the wheel is a drive wheel it only gets the rotation that visualizes speed.
			// If we are hand braking we don't rotate it.
			w.tireGraphic.Rotate(Vector3.right * (w.groundSpeed.z / wheelRadius) * Time.deltaTime * Mathf.Rad2Deg);
		}
	}
}

void  UpdateGear ( Vector3 relativeVelocity  ){
	currentGear = 0;
	for(int i= 0; i < numberOfGears - 1; i++)
	{
		if(relativeVelocity.z > gearSpeeds[i])
			currentGear = i + 1;
	}
}

/**************************************************/
/* Functions called from FixedUpdate()            */
/**************************************************/

void  UpdateDrag ( Vector3 relativeVelocity  ){
	Vector3 relativeDrag = new Vector3(	-relativeVelocity.x * Mathf.Abs(relativeVelocity.x), 
												-relativeVelocity.y * Mathf.Abs(relativeVelocity.y), 
												-relativeVelocity.z * Mathf.Abs(relativeVelocity.z) );
	
	Vector3 drag= Vector3.Scale(dragMultiplier, relativeDrag);
		
	if(initialDragMultiplierX > dragMultiplier.x) // Handbrake code
	{			
		drag.x /= (relativeVelocity.magnitude / (topSpeed / ( 1 + 2 * handbrakeXDragFactor ) ) );
		drag.z *= (1 + Mathf.Abs(Vector3.Dot(rigidbody.velocity.normalized, transform.forward)));
		drag += rigidbody.velocity * Mathf.Clamp01(rigidbody.velocity.magnitude / topSpeed);
	}
	else // No handbrake
	{
		drag.x *= topSpeed / relativeVelocity.magnitude;
	}
	
	if(Mathf.Abs(relativeVelocity.x) < 5 && !handbrake)
		drag.x = -relativeVelocity.x * dragMultiplier.x;
		

	rigidbody.AddForce(transform.TransformDirection(drag) * rigidbody.mass * Time.deltaTime);
}

void  UpdateFriction ( Vector3 relativeVelocity  ){
	float sqrVel = relativeVelocity.x * relativeVelocity.x;
	
	// Add extra sideways friction based on the car's turning velocity to avoid slipping
	wfc.extremumValue = Mathf.Clamp(300 - sqrVel, 0, 300);
	wfc.asymptoteValue = Mathf.Clamp(150 - (sqrVel / 2), 0, 150);
		
	foreach(Wheel w in wheels)
	{
		w.collider.sidewaysFriction = wfc;
		w.collider.forwardFriction = wfc;
	}
}

void  CalculateEnginePower ( Vector3 relativeVelocity  ){
	if(throttle == 0)
	{
		currentEnginePower -= Time.deltaTime * 200;
	}
	else if( HaveTheSameSign(relativeVelocity.z, throttle) )
	{
		float normPower = (currentEnginePower / engineForceValues[engineForceValues.Length - 1]) * 2;
		currentEnginePower += Time.deltaTime * 200 * EvaluateNormPower(normPower);
	}
	else
	{
		currentEnginePower -= Time.deltaTime * 300;
	}
	
	if(currentGear == 0)
		currentEnginePower = Mathf.Clamp(currentEnginePower, 0, engineForceValues[0]);
	else
		currentEnginePower = Mathf.Clamp(currentEnginePower, engineForceValues[currentGear - 1], engineForceValues[currentGear]);
}

void  CalculateState (){
	canDrive = false;
	canSteer = false;
	
	foreach(Wheel w in wheels)
	{
		if(w.collider.isGrounded)
		{
			if(w.steerWheel)
				canSteer = true;
			if(w.driveWheel)
				canDrive = true;
		}
	}
}

void  ApplyThrottle ( bool canDrive ,    Vector3 relativeVelocity  ){
	if(canDrive)
	{
		float throttleForce = 0;
		float brakeForce = 0;
		
		if (HaveTheSameSign(relativeVelocity.z, throttle))
		{
			if (!handbrake)
				throttleForce = Mathf.Sign(throttle) * currentEnginePower * rigidbody.mass;
		}
		else
			brakeForce = Mathf.Sign(throttle) * engineForceValues[0] * rigidbody.mass;
		
		rigidbody.AddForce(transform.forward * Time.deltaTime * (throttleForce + brakeForce));
	}
}

void  ApplySteering ( bool canSteer ,    Vector3 relativeVelocity  ){
	if(canSteer)
	{
		float turnRadius = 3.0f / Mathf.Sin((90 - (steer * 30)) * Mathf.Deg2Rad);
		float minMaxTurn = EvaluateSpeedToTurn(rigidbody.velocity.magnitude);
		float turnSpeed = Mathf.Clamp(relativeVelocity.z / turnRadius, -minMaxTurn / 10, minMaxTurn / 10);
		
		transform.RotateAround(	transform.position + transform.right * turnRadius * steer, 
								transform.up, 
								turnSpeed * Mathf.Rad2Deg * Time.deltaTime * steer);
		
		Vector3 debugStartPoint= transform.position + transform.right * turnRadius * steer;
		Vector3 debugEndPoint= debugStartPoint + Vector3.up * 5;
		
		Debug.DrawLine(debugStartPoint, debugEndPoint, Color.red);
		
		if(initialDragMultiplierX > dragMultiplier.x) // Handbrake
		{
			float rotationDirection = Mathf.Sign(steer); // rotationDirection is -1 or 1 by default, depending on steering
			if(steer == 0)
			{
				if(rigidbody.angularVelocity.y < 1) // If we are not steering and we are handbraking and not rotating fast, we apply a random rotationDirection
					rotationDirection = Random.Range(-1.0f, 1.0f);
				else
					rotationDirection = rigidbody.angularVelocity.y; // If we are rotating fast we are applying that rotation to the car
			}
			// -- Finally we apply this rotation around a point between the cars front wheels.
			transform.RotateAround( transform.TransformPoint( (	frontWheels[0].localPosition + frontWheels[1].localPosition) * 0.5f), 
																transform.up, 
																rigidbody.velocity.magnitude * Mathf.Clamp01(1 - rigidbody.velocity.magnitude / topSpeed) * rotationDirection * Time.deltaTime * 2);
		}
	}
}

/**************************************************/
/*               Utility Functions                */
/**************************************************/

float Convert_Miles_Per_Hour_To_Meters_Per_Second ( float value  ){
	return value * 0.44704f;
}

float Convert_Meters_Per_Second_To_Miles_Per_Hour ( float value  ){
	return value * 2.23693629f;	
}

bool HaveTheSameSign ( float first ,   float second  ){
	 if (Mathf.Sign(first) == Mathf.Sign(second))
		return true;
	else
		return false;
}

float  EvaluateSpeedToTurn ( float speed  ){
	if(speed > topSpeed / 2)
		return minimumTurn;
	
	float speedIndex = 1 - (speed / (topSpeed / 2));
	return minimumTurn + speedIndex * (maximumTurn - minimumTurn);
}

float  EvaluateNormPower ( float normPower  ){
	if(normPower < 1)
		return 10 - normPower * 9;
	else
		return 1.9f - normPower * 0.9f;
}

/*int  GetGearState (){
	Vector3 relativeVelocity = transform.InverseTransformDirection(rigidbody.velocity);
	float lowLimit = (currentGear == 0 ? 0 : gearSpeeds[currentGear-1]);
	return ((relativeVelocity.z - lowLimit) / (gearSpeeds[currentGear - lowLimit]) * (1 - currentGear * 0.1f) + currentGear * 0.1f);
}*/

}