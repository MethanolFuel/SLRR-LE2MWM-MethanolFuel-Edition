package java.lang;
import system.*;
import java.io.*;
class RawEdit
{
	public static IntValueAddress Address_I_SoftwareFrustrumCullReturnOverwrite 		= new IntValueAddress(0x6185BC);
	public static IntValueAddress Address_I_DisableFlop 														= new IntValueAddress(0x6185D0);
	public static IntValueAddress Address_I_cfg_mem_instance_current 								= new IntValueAddress(0x765F60);
	public static IntValueAddress Address_I_cfg_mem_instance_max 										= new IntValueAddress(0x618D5C);
	public static IntValueAddress Address_I_cfg_mem_instance_min 										= new IntValueAddress(0x618D60);
	public static IntValueAddress Address_I_cfg_mem_sound_max 											= new IntValueAddress(0x618D64);
	public static IntValueAddress Address_I_cfg_mem_sound_min 											= new IntValueAddress(0x618D68);
	public static IntValueAddress Address_I_cfg_mem_texture_current 								= new IntValueAddress(0x65C234);
	public static IntValueAddress Address_I_cfg_mem_texture_max 										= new IntValueAddress(0x618D54);
	public static IntValueAddress Address_I_cfg_mem_texture_min 										= new IntValueAddress(0x618D58);
	public static IntValueAddress Address_I_cfg_mem_vertex_current 									= new IntValueAddress(0x65BD44);
	public static IntValueAddress Address_I_cfg_mem_vertex_max 											= new IntValueAddress(0x618D4C);
	public static IntValueAddress Address_I_cfg_mem_vertex_min 											= new IntValueAddress(0x618D50);
	public static IntValueAddress Address_I_cfg_skidmarks_max 											= new IntValueAddress(0x618BF4);
	public static IntValueAddress Address_I_resource_loadrate 											= new IntValueAddress(0x618D6C);
	public static IntValueAddress Address_I_AISlideIgnoreMod 												= new IntValueAddress(0x6185E8);
	public static IntValueAddress Address_I_DisableCruiseSteerOverWrite 						= new IntValueAddress(0x618038);
	
	public static FloatValueAddress Address_F_ClutchOnInAutoShiftDeadTime 					= new FloatValueAddress(0x618610);
	public static FloatValueAddress Address_F_ThrottleOnInAutoShiftDeadTime 				= new FloatValueAddress(0x618614);
	public static FloatValueAddress Address_F_CarDeformationOnCollisionMult 				= new FloatValueAddress(0x6185C4);
	public static FloatValueAddress Address_F_CarDeformationOnCollisionMultMinus 		= new FloatValueAddress(0x6185C0);// this should be set at the same time as the above one to CarDeformationOnCollisionMult*-1
	public static FloatValueAddress Address_F_CarPartDetachmentMult 								= new FloatValueAddress(0x6185C8);
	public static FloatValueAddress Address_F_CarChassisDeformationOnCollisionMult 	= new FloatValueAddress(0x6185CC);
	public static FloatValueAddress Address_F_WearMultEngine 												= new FloatValueAddress(0x6185D4);
	public static FloatValueAddress Address_F_WearMultTyre 													= new FloatValueAddress(0x6185E0);
	public static FloatValueAddress Address_F_TrafficFlatBonus 											= new FloatValueAddress(0x6185DC);
	public static FloatValueAddress Address_F_AIOverSteering 												= new FloatValueAddress(0x6185EC);
	public static FloatValueAddress Address_F_AIOverBreaking 												= new FloatValueAddress(0x6185F0);
	public static FloatValueAddress Address_F_AIOverThrotlling 											= new FloatValueAddress(0x6185F4);
	public static FloatValueAddress Address_F_cfg_deformation 											= new FloatValueAddress(0x611360);
	public static FloatValueAddress Address_F_cfg_engine_inertia_factor 						= new FloatValueAddress(0x60E028);
	public static FloatValueAddress Address_F_cfg_external_damage 									= new FloatValueAddress(0x611358);
	public static FloatValueAddress Address_F_cfg_internal_damage 									= new FloatValueAddress(0x61135C);
	public static FloatValueAddress Address_F_cfg_particle_density 									= new FloatValueAddress(0x618948);
	
	public static IntValueAddress Address_I_bDrawHeadlightRays 											= new IntValueAddress(0x6187BC);
	public static IntValueAddress Address_I_bDrawHeadlightFlares 										= new IntValueAddress(0x6187C0);
	public static IntValueAddress Address_I_ShadowSize 															= new IntValueAddress(0x6187B8);
	public static IntValueAddress Address_I_Shadows 																= new IntValueAddress(0x6187B4);
	public static IntValueAddress Address_I_TextureSize 														= new IntValueAddress(0x6188B4);
	
	public static FloatValueAddress Address_F_cfg_ShadowDetail 											= new FloatValueAddress(0x65AED0);
	public static FloatValueAddress Address_F_cfg_ObjectDetail 											= new FloatValueAddress(0x65C424);
	public static FloatValueAddress Address_F_cfg_VideoGamma 												= new FloatValueAddress(0x617728);
	public static FloatValueAddress Address_F_WheelGroundFeedbackFactor 						= new FloatValueAddress(0x60E020);
	public static FloatValueAddress Address_F_WheelBreakeFactor 										= new FloatValueAddress(0x60E024);
	public static FloatValueAddress Address_F_LastFrameDrawTime 										= new FloatValueAddress(0x63C528);
	public static FloatValueAddress Address_F_GameTimeSeconds 											= new FloatValueAddress(0x63C530);
	
	public static FloatValueAddress Address_F_Gravity 															= new FloatValueAddress(0x6186E4);
	public static FloatValueAddress Address_F_CollisionForceMultMapObjects_1 				= new FloatValueAddress(0x6186EC);
	public static FloatValueAddress Address_F_CollisionForceMultMapObjects_2 				= new FloatValueAddress(0x6186F0);
	public static FloatValueAddress Address_F_CollisionForceMultVehicle_1 					= new FloatValueAddress(0x6186F4);
	public static FloatValueAddress Address_F_CollisionForceMultVehicle_2 					= new FloatValueAddress(0x6186F8);
	public static FloatValueAddress Address_F_FrictionOnCollision_1 								= new FloatValueAddress(0x6186FC);
	public static FloatValueAddress Address_F_FrictionOnCollision_2 								= new FloatValueAddress(0x618700);
	public static FloatValueAddress Address_F_WheelFrictionMult 										= new FloatValueAddress(0x618710);
	public static FloatValueAddress Address_F_WheelFriction_XMult 									= new FloatValueAddress(0x617FC4);
	public static FloatValueAddress Address_F_WheelSilictionMult 										= new FloatValueAddress(0x617FC8);
	public static FloatValueAddress Address_F_WheelStiffnessMult 										= new FloatValueAddress(0x618000);
	public static FloatValueAddress Address_F_WheelRollResMult 											= new FloatValueAddress(0x618004);
	public static FloatValueAddress Address_F_WheelBearingMult 											= new FloatValueAddress(0x618008);
	public static FloatValueAddress Address_F_WheelMaxLoadMult 											= new FloatValueAddress(0x61800C);
	public static FloatValueAddress Address_F_WheelLoadSmoothMult 									= new FloatValueAddress(0x618010);
	public static FloatValueAddress Address_F_NativeSpring_Var1 										= new FloatValueAddress(0x618014);//(hardness)
	public static FloatValueAddress Address_F_NativeSpring_Var2 										= new FloatValueAddress(0x618018);//(inverse-rebound)
	public static FloatValueAddress Address_F_NativeSpring_Var3 										= new FloatValueAddress(0x61801C);//(inbound inverse-softness)
	public static FloatValueAddress Address_F_NativeSpring_Var4 										= new FloatValueAddress(0x618020);//(length and bumpiness)
	public static FloatValueAddress Address_F_NativeSpring_Var5 										= new FloatValueAddress(0x618024);//(max inverse-contraction)
	public static FloatValueAddress Address_F_NativeSpring_Var6 										= new FloatValueAddress(0x618028);//(collision realted)
	public static FloatValueAddress Address_F_NativeSpring_Var8 										= new FloatValueAddress(0x61802C);//(spring length)
	public static FloatValueAddress Address_F_CruiseBreakWhenOverLimit 							= new FloatValueAddress(0x618034);
	
	public static int GetIntFromRawAddress(IntValueAddress address)
	{
		return getI(address.address);
	}
	public static float GetFloatFromRawAddress(FloatValueAddress address)
	{
		return getF(address.address);
	}
	public static void SetFloatOnRawAddress(FloatValueAddress address,float value)
	{
		setF(address.address,value);
	}
	public static void SetIntOnRawAddress(IntValueAddress address,int value)
	{
		setI(address.address,value);
	}
	static native int getI(int address);
	static native float getF(int address);
	static native int setF(int address,float value); //returns the address overwritten
	static native int setI(int address,int value); //returns the address overwritten
	static native int CentCur(int notUsed); // centers the cursor on the client window the param and return is not used (return is the new x position of the cursor) (I just wanted to reuse the signature string of the getI native function)
}

class IntValueAddress
{
	public int address;
	public IntValueAddress(int addr)
	{
		address = addr;
	}
}
class FloatValueAddress
{
	public int address;
	public FloatValueAddress(int addr)
	{
		address = addr;
	}
}