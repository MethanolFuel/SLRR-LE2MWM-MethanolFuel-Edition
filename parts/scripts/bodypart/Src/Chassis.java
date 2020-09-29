package java.game.parts.bodypart;

import java.io.*;
import java.util.*;
import java.util.resource.*;
import java.render.osd.*;
import java.game.*;
import java.game.cars.*;
import java.game.parts.*;
import java.game.parts.enginepart.*;
import java.game.parts.rgearpart.*;
import java.game.parts.rgearpart.reciprocatingrgearpart.*;
import java.game.parts.enginepart.airfueldeliverysystem.*;
import java.game.parts.enginepart.slidingenginepart.reciprocatingenginepart.*;

public class Chassis extends BodyPart
{
	//own vars
	float	max_steer = 0.4;

	//vars from engine block
	float	engine_torque = 0.0;
	float	engine_torque2 = 0.0;
//	float[]	torquetable = new float[16];
//	float[]	torquetable2 = new float[16];
//	float	table_stepsize = 500;	//torquetable stepsize
	float	engine_inertia = 10.0;
	float	maxRPM = 7000.0;
	float	RPM_limit = 20000.0;
	float	engine_friction_fwd = 0.0001;
	float	engine_friction_rev = 0.005;
	float	engine_mass = 40.0;
	float	engine_rpm_idle = 800.0;
	float	starter_torque = 0;
	float	fully_stripped_drag = 0.333;

	static int	MAKE_CUSTOM		= 0;
	static int	MAKE_BAIERN		= 1;
	static int	MAKE_DUHEN		= 2;
	static int	MAKE_EINVAGEN		= 3;
	static int	MAKE_ISHIMA		= 4;
	static int	MAKE_SHIMUTSHIBU	= 5;
	static int	MAKE_MC			= 6;
	static int	MAKE_EMER		= 7;
	static int	MAKE_HAULER_S_HEAVEN	= 8;
	static int	MAKE_PRIME		= 9;

	static int	MODEL_UNDEFINED		= -1;

	int	make = MAKE_CUSTOM;
	int	model = MODEL_UNDEFINED;

	final static int	DT_FWD = 1;
	final static int	DT_RWD = 2;
	int	drive_type;

	float	brake_balance = 0.5;
	int	brake_balance_can_be_set = 0;

	Vector	exhaustSlotIDList = null;

	float	diff_lock = 0.0;

	//vars from transmission
	int		gears = 0;
	float[]	ratio = new float[8];
	float	rearend_ratio = 3.3;

	float	ClutchF = 500.0;

	float	tank_nitro = 0.0;
	float	consumption_nitro = 0.0;

	//vars from wheels,brakes,suspensions
	int		wheels = 4;

	String	vehicleName = "unnamed";
	String	makerName = "unnamed";

	//vars calculated from body parts
	Vector3	wing_dir;		//fwd. dir. of wing profile
	Vector3	wing_F;			//pos and size of lift

//	int carID = Car.COMMON;

//	int		SFX_engine_up	=sound:0x00000003r;
//	int		SFX_engine_down	=sound:0x00000002r;
//	int		SFX_engine_idle	=sound:0x00000001r;
	int		SFX_trans_fwd	=sound:0x00000007r;
	int		SFX_trans_rev	=sound:0x00000009r;
	int		SFX_ignition	=sound:0x0000000Ar;
//	int		SFX_horn	=sound:0x00000015r;

//	float	rpm_engine_up	= 5000.0;
//	float	rpm_engine_down	= 2500.0;
//	float	rpm_engine_idle	=  700.0;
	float	rpm_trans_fwd	= 1400.0;
	float	rpm_trans_rev	= 1400.0;
	float	sfx_starter_rpm	=  200.0;
//	float	sfx_horn_pitch	=    1.0;

	int	suspend_update = 0;

	int[]	stock_parts_list_E  = null;
	int[]	stg_1_parts_list_E  = null;
	int[]	stg_2_parts_list_E  = null;
	int[]	parts_list_E  = null;

	float	stg_1_engne_kit_limit = 1.33333333;
	float	stg_2_engne_kit_limit = 1.66666666;

	int[]	stock_parts_list_FL = null;
	int[]	stock_parts_list_FR = null;
	int[]	stock_parts_list_RL = null;
	int[]	stock_parts_list_RR = null;
	int[]	stock_parts_list_F  = null;
	int[]	stock_parts_list_Rr = null;
	int[]	stock_parts_list_L  = null;
	int[]	stock_parts_list_R  = null;
	int[]	stock_parts_list_T  = null;

	int[]	stg_1_parts_list_FL = null;
	int[]	stg_1_parts_list_FR = null;
	int[]	stg_1_parts_list_RL = null;
	int[]	stg_1_parts_list_RR = null;
	int[]	stg_1_parts_list_F  = null;
	int[]	stg_1_parts_list_Rr = null;
	int[]	stg_1_parts_list_L  = null;
	int[]	stg_1_parts_list_R  = null;
	int[]	stg_1_parts_list_T  = null;

	int[]	stg_2_parts_list_FL = null;
	int[]	stg_2_parts_list_FR = null;
	int[]	stg_2_parts_list_RL = null;
	int[]	stg_2_parts_list_RR = null;
	int[]	stg_2_parts_list_F  = null;
	int[]	stg_2_parts_list_Rr = null;
	int[]	stg_2_parts_list_L  = null;
	int[]	stg_2_parts_list_R  = null;
	int[]	stg_2_parts_list_T  = null;

	int[]	parts_list_FL = null;
	int[]	parts_list_FR = null;
	int[]	parts_list_RL = null;
	int[]	parts_list_RR = null;
	int[]	parts_list_F  = null;
	int[]	parts_list_Rr = null;
	int[]	parts_list_L  = null;
	int[]	parts_list_R  = null;
	int[]	parts_list_T  = null;

	float	stg_1_body_kit_limit = 1.33333333;
	float	stg_2_body_kit_limit = 1.66666666;

	int[]	stock_parts_list_RGear_suspensions = null;
	int[]	stock_parts_list_RGear_shocks = null;
	int[]	stock_parts_list_RGear_springs = null;
	int[]	stock_parts_list_RGear_brakes = null;
	int[]	stock_parts_list_RGear_sways = null;
	int[]	stock_parts_list_RGear_wheels = null;
	int[]	stock_parts_list_RGear_tyres = null;
	int[]	stock_parts_list_RGear_others = null;

	int[]	stg_1_parts_list_RGear_suspensions = null;
	int[]	stg_1_parts_list_RGear_shocks = null;
	int[]	stg_1_parts_list_RGear_springs = null;
	int[]	stg_1_parts_list_RGear_brakes = null;
	int[]	stg_1_parts_list_RGear_sways = null;
	int[]	stg_1_parts_list_RGear_wheels = null;
	int[]	stg_1_parts_list_RGear_tyres = null;
	int[]	stg_1_parts_list_RGear_others = null;

	int[]	stg_2_parts_list_RGear_suspensions = null;
	int[]	stg_2_parts_list_RGear_shocks = null;
	int[]	stg_2_parts_list_RGear_springs = null;
	int[]	stg_2_parts_list_RGear_brakes = null;
	int[]	stg_2_parts_list_RGear_sways = null;
	int[]	stg_2_parts_list_RGear_wheels = null;
	int[]	stg_2_parts_list_RGear_tyres = null;
	int[]	stg_2_parts_list_RGear_others = null;

	int[]	parts_list_RGear_suspensions = null;
	int[]	parts_list_RGear_shocks = null;
	int[]	parts_list_RGear_springs = null;
	int[]	parts_list_RGear_brakes = null;
	int[]	parts_list_RGear_sways = null;
	int[]	parts_list_RGear_wheels = null;
	int[]	parts_list_RGear_tyres = null;
	int[]	parts_list_RGear_others = null;

	float	stg_1_rgear_kit_limit = 1.33333333;
	float	stg_2_rgear_kit_limit = 1.66666666;

	int[]	parts_list  = null;
	
	public Vector PneumaticSA = new Vector();
	public void Hsuspension( int action, int type )
	{
		for( int i=0; i<=PneumaticSA.size()-1; i++ )
		{
			if( PneumaticSA.elementData[i] && PneumaticSA.elementData[i] instanceof Pneumatic_ShockAbsorber )
			{
				PneumaticSA.elementData[i].Use( action, type );
			}
		}
	}

	public Chassis( int id )
	{
		super( id );

		name = "chassis";

		C_drag = 0.32;
		drag_center = new Vector3(0,0,0);
		suspend_update = 0;

		prestige_calc_weight = 90.0;
		
		PneumaticSA = new Vector(1);// New colector, put 1 empty object to it
// 		AnimateParts = new Vector(1);// New colector, put 1 empty object to it
	}

	public void finalize()
	{
		clearEventMask( EVENT_ANY );
		removeAllTimers();
		unregisterCallbacks();
	}

	public void save( File saveGame )
	{
		super.save( saveGame );

		int save_ver = 6;

		saveGame.write(save_ver);

		if (save_ver >= 1)
		{
			int wheels = 4;
			saveGame.write( wheels );
			while( wheels-- )
			{
				saveGame.write( getWheelDamage( wheels ));
			}
		}
		if (save_ver >= 2)
		{
			saveGame.write( brake_balance );
		}
		if (save_ver >= 3)
		{
			float m = getMileage();
			saveGame.write( m );
		}
		if (save_ver >= 4)
		{
			saveGame.write( WheelPosXMultF );
			saveGame.write( WheelPosYMultF );
			saveGame.write( WheelPosXMultR );
			saveGame.write( WheelPosYMultR );
			saveGame.write( RimPosXMultF );
			saveGame.write( RimPosXMultR );
			saveGame.write( ForceFrontWheelDrive );
			saveGame.write( blockPosX );
			saveGame.write( blockPosY );
			saveGame.write( blockPosZ );
			saveGame.write( blockRotY );
			
			saveGame.write( WheelRotYF );
			saveGame.write( WheelRotYR );
			
			saveGame.write( fallBackEngineSound );
		}
		if (save_ver >= 5)
		{
			saveGame.write( ForcedDriveDistribution );
		}
		if (save_ver >= 6)
		{
			saveGame.write( FrontMaxSteer );
			saveGame.write( RearMaxSteer );
		}
	}


	public void load( File saveGame )
	{
		suspend_update=1;
		FrontMaxSteer = 1.48;

		super.load( saveGame );

		int save_ver = saveGame.readInt();

		if (save_ver >= 1)
		{
			int wheels = saveGame.readInt();
			while( wheels-- )
			{
				setWheelDamage( wheels, saveGame.readString() );
			}
		}
		if (save_ver >= 2)
		{
			brake_balance = saveGame.readFloat();
		}
		if (save_ver >= 3)
		{
			float m = saveGame.readFloat();
			setMileage ( m );
		}
		if (save_ver >= 4)
		{
			WheelPosXMultF = saveGame.readFloat();
			WheelPosYMultF = saveGame.readFloat();
			WheelPosXMultR = saveGame.readFloat();
			WheelPosYMultR = saveGame.readFloat();
			RimPosXMultF = saveGame.readFloat();
			RimPosXMultR = saveGame.readFloat();
			ForceFrontWheelDrive = saveGame.readInt();
			blockPosX = saveGame.readFloat();
			blockPosY = saveGame.readFloat();
			blockPosZ = saveGame.readFloat();
			blockRotY = saveGame.readFloat();
			
			WheelRotYF = saveGame.readFloat();
			WheelRotYR = saveGame.readFloat();
			
			fallBackEngineSound = saveGame.readInt();
		}
		if (save_ver >= 5)
		{
			ForcedDriveDistribution = saveGame.readFloat();
		}
		if (save_ver >= 6)
		{
			FrontMaxSteer = saveGame.readFloat();
			RearMaxSteer = saveGame.readFloat();
		}

		suspend_update=0;
		updatevariables();
		forceUpdate();
	}

	public float calcPoliceFine( float thoroughness )
	{
		float fine = super.calcPoliceFine(thoroughness);

		for( int i=attachedParts.size()-1; i>=0; i-- )
		{
			fine += attachedParts.elementAt(i).calcPoliceFine(thoroughness);
		}

		return fine;
	}

	public float maxHP;
	DynoData dynodata = null;
	public void updatevariables()
	{
		C_drag = fully_stripped_drag;
		diff_lock = 0.0;
		updateDifflock();

		WheelRef whl;

		drive_type = 0;
		int i;
		int j;
		for (i=0; i<4; i++)
		{
			whl = getWheel(i);
			if (whl)
			{
				whl.setDrive(0.0);
				whl.setInstantCenter( 10000.0, 10000.0, 10000.0, 10000.0, 10000.0, 10000.0 );
			}
		}
	
		super.updatevariables();

		// collect chassis specific police fine values //

		police_check_fine_value = 0;
		if (exhaustSlotIDList)
			for (i=0; i<exhaustSlotIDList.size(); i++)
			{
				Part p = partOnSlot(exhaustSlotIDList.elementAt(i).intValue());
				if (!p || !(p instanceof ExhaustPipe))
					police_check_fine_value += 200;
				else
				{
					ExhaustPipe ep = (ExhaustPipe)p;
					ep.police_check_fine_value = 0;
					if (ep.mufflerSlotIDList)
					for (j=0; j<ep.mufflerSlotIDList.size(); j++)
					{
						Part m = ep.partOnSlot(ep.mufflerSlotIDList.elementAt(j).intValue());
						if (!m || !(m instanceof ExhaustTip))
							ep.police_check_fine_value += 100;
					}
				}
			}

		RPM_limit = 12000.0;

		Part other;
		int[] has = new int[5];

//		System.log("--- start of wheel updates ---");

		for(int i=0; i<wheels; i++)
		{
//			System.log(" wheel #"+i);

			has[0] = 0;
			has[1] = 0;
			has[2] = 0;
			has[3] = 0;
			has[4] = 0;

			other = partOnSlot(111+i);
			if (other && other instanceof Brake)
			{
				other.updatevariables();
				has[1] = 1;
			}

			other = partOnSlot(101+i);
			int wheelID = 0;
			if (other && other instanceof Wheel)
			{
				wheelID = other.getInfo( GII_TYPE );
				other.updatevariables();
				has[2] = 1;
			}

			other = partOnSlot(311+i);
			if (other && other instanceof Spring)
			{
				other.updatevariables();
				has[4] = 1;
			}

			other = partOnSlot(301+i);
			if (other && other instanceof ShockAbsorber)
			{
				other.updatevariables();
				has[3] = 1;
			}
			if(other && other instanceof Pneumatic_ShockAbsorber)
			{
				other.updatevariables();
				has[3] = 1;
				other.CheckType();
			}

			other = partOnSlot(121+i);
			if (other && other instanceof Suspension)
			{
				has[0] = 1;
				other.updatevariables();
			}

			// 0-suspension, 1-brake, 2-rim, 3-shock, 4-spring //
			if (has[2]) // the rim is put on, so the rim and the tyre can be changed //
			{
				// enable these //
				disableSlot(101+i,0); // rim

				// disable these //
				disableSlot(111+i,1); // brake
				disableSlot(311+i,1); // spring
				disableSlot(301+i,1); // shock
				disableSlot(121+i,1); // suspension
			}
			else
			if (has[1]) // the brake is put on, so the rim and the brake can be changed //
			{
				// enable these //
				disableSlot(101+i,0); // rim
				disableSlot(111+i,0); // brake

				// disable these //
				disableSlot(311+i,1); // spring
				disableSlot(301+i,1); // shock
				disableSlot(121+i,1); // suspension
			}
			else
			if (has[4]) // the spring is put on, so the spring and the shock can be changed //
			{
				// disable these //
				disableSlot(101+i,1); // rim

				// enable these //
				disableSlot(111+i,0); // brake
				disableSlot(311+i,0); // spring

				// disable these //
				disableSlot(301+i,1); // shock
				disableSlot(121+i,1); // suspension
			}
			else
			if (has[3]) // the shock is put on, so the shock and the spring can be changed //
			{
				// disable these //
				disableSlot(101+i,1); // rim
				disableSlot(111+i,1); // brake

				// enable these //
				disableSlot(311+i,0); // spring
				disableSlot(301+i,0); // shock

				// disable these //
				disableSlot(121+i,1); // suspension
			}
			else
			if (has[0]) // the suspension is put on, so the shock and the suspension can be changed //
			{
				// disable these //
				disableSlot(101+i,1); // rim
				disableSlot(111+i,1); // brake
				disableSlot(311+i,1); // spring

				// enable these //
				disableSlot(301+i,0); // shock
				disableSlot(121+i,0); // suspension
			}
			else
			{
				// disable these //
				disableSlot(101+i,1); // rim
				disableSlot(111+i,1); // brake
				disableSlot(311+i,1); // spring
				disableSlot(301+i,1); // shock

				// enable these //
				disableSlot(121+i,0); // suspension
			}
			
			WheelRef	whl = getWheel(i);
			Suspension sus = partOnSlot(121+i);
			if(whl != null)
			{
				float currentSlotX = 0.0;
				Vector3 pos = whl.getPos();
				Ypr rot = new Ypr();
				pos.z = 0;
				if(i == 0 || i == 1)
				{
					if(pos.x < 0)
					{
						pos.x = -WheelPosXMultF;
						rot.r = -deg2rad(WheelRotYF);
						currentSlotX = -(RimPosXMultF+RimPosXMultFMinOffset);
						RawEdit.setF(whl.ptr+0xc8,FrontMaxSteer);
					}
					else
					{
						pos.x = WheelPosXMultF;
						rot.r = deg2rad(WheelRotYF);
						currentSlotX = (RimPosXMultF+RimPosXMultFMinOffset);
						RawEdit.setF(whl.ptr+0xc8,FrontMaxSteer);
					}
					pos.y = WheelPosYMultF;
				}
				else
				{
					if(pos.x < 0)
					{
						pos.x = -WheelPosXMultR;
						rot.r = -deg2rad(WheelRotYR);
						currentSlotX = -(RimPosXMultR+RimPosXMultRMinOffset);
						RawEdit.setF(whl.ptr+0xc8,RearMaxSteer);
					}
					else
					{
						pos.x = WheelPosXMultR;
						rot.r = deg2rad(WheelRotYR);
						currentSlotX = (RimPosXMultR+RimPosXMultRMinOffset);
						RawEdit.setF(whl.ptr+0xc8,RearMaxSteer);
					}
					pos.y = WheelPosYMultR;
				}
				whl.setPos(pos);
				//whl.setYpr(rot);
				//UpdateCamber(i);
				setSlotPos( 101+i, new Vector3(currentSlotX, 0, 0), null );
			}
		}
		PlaceRollPreventionBoxes();
		SetFrontWheelDrive();

//		System.log("--- end of wheel updates ---");

		Part part = null;
		Block engine = null;

		int slotIndex;
		int slotID;
		int slots = getSlots();

		for( slotIndex=0; slotIndex<slots; slotIndex++ )
		{
			slotID = getSlotID( slotIndex );
			part = partOnSlot(slotID);

			if (part)
			{
				if (part instanceof Block)
				{
					engine = part;
				}
				if (part instanceof BodyPart)
				{
					BodyPart p = (BodyPart)part;
					C_drag -= p.drag_reduction;
				}
			}
		}

		if (C_drag < 0.0)
			C_drag = 0.0;

		if (engine)
		{
			engine.updatevariables();	// calc. dynodata

//			SFX_trans_fwd = engine.SFX_trans_fwd;
//			SFX_trans_rev = engine.SFX_trans_rev;
//			SFX_ignition = engine.SFX_ignition;

//			rpm_trans_fwd = engine.rpm_trans_fwd;
//			rpm_trans_rev = engine.rpm_trans_rev;
//	       		sfx_starter_rpm = engine.sfx_starter_rpm;

			DynoData dyno = engine.dynodata;
			
			int RPMstep = 250;

			RPM_limit = dyno.RPM_limit;
			if (dyno.maxRPM < 100)
				dyno.maxRPM = dyno.RPM_limit*1.25;
			int desired_steps = dyno.maxRPM/RPMstep;
			dyno.maxRPM = desired_steps*RPMstep;
			dyno.calcDyno ( desired_steps );
//			dyno.LogVars();

			engine_torque = dyno.torque;
			engine_torque2 = dyno.torque2;
			maxRPM = dyno.maxRPM;

			engine_inertia = engine.inertia;
			engine_friction_fwd = engine.friction_fwd;
			engine_friction_rev = engine.friction_rev;
			engine_rpm_idle = engine.rpm_idle;

			consumption_nitro = dyno.nitro_consumption;
			
			maxHP = dyno.maxHP;
			dynodata = dyno;
		} else
		{
			engine_torque = 0.0f;
			engine_torque2 = 0.0f;
			engine_inertia = 10.0f;
			maxRPM = 7000.0;
			engine_friction_fwd = 0.0001;
			engine_friction_rev = 0.005;
			starter_torque = 0;

			gears = 0;
			ratio[0] = 0.0;
			ratio[1] = 3.5;
			ratio[2] = 2.7;
			ratio[3] = 1.9;
			ratio[4] = 1.3;
			ratio[5] = 0.0;
			ratio[6] = 0.0;
			ratio[7] = -4.0;
		}
		
//		System.log("chassis: engine_inertia = "+engine_inertia);
//		System.log("chassis: engine_friction_fwd = "+engine_friction_fwd);
//		System.log("chassis: engine_friction_rev = "+engine_friction_rev);

		other = partOnSlot(999); // steering wheel //
		if (other)
		{
			other.updatevariables();
		}

		tank_nitro = 0.0;
		for(int i=500; i<510; i++)
		{
			other = partOnSlot(i); // nitro tanks //
			if (other && other instanceof Canister)
			{
				other.updatevariables();
				tank_nitro += ((Canister)other).capacity;
			}
		}
		
		 UpdateOnlyBlock();
	}
	//for used car generator: creates parts for 'required' slots, sets default color (from a set)
	public void addStockParts()
	{
		addStockParts( GameLogic.CARCOLORS[0], 2.0*Math.random(), 2.0*Math.random() );
	}

	int buildOnlyStockSuspensions = 0;
	public void addStockParts( Descriptor desc )
	{
//		super.addStockParts( desc );

		int TextureID = desc.color;
		setTexture( TextureID );

/////////////////////// ToDo: Descriptor.power should be completeness_level ///////////////////////
///////////////////////// ToDo: Descriptor.optical should be tuning_level /////////////////////////

		int crash_count = (1-desc.tear) * 30;//crash_times * 10; // * desc.crash_count_override; //
                                              
		int part_looper;
		float CRASH_BADNESS_DAMPING = 0.79432823472428150206591828283639; // drops to 0.1 in five steps == inv 0.1^5//
		Vector partLists = new Vector();
		Descriptor d;

		if(buildOnlyStockSuspensions == 0)
		{
			parts_list_E  = stock_parts_list_E;
			parts_list_FL = stock_parts_list_FL;
			parts_list_FR = stock_parts_list_FR;
			parts_list_RL = stock_parts_list_RL;
			parts_list_RR = stock_parts_list_RR;
			parts_list_F  = stock_parts_list_F;
			parts_list_Rr = stock_parts_list_Rr;
			parts_list_L  = stock_parts_list_L;
			parts_list_R  = stock_parts_list_R;
			parts_list_T  = stock_parts_list_T;

			if (desc.power >= stg_2_engne_kit_limit)
				if (stg_2_parts_list_E)  parts_list_E  = stg_2_parts_list_E;
			else
			if (desc.power >= stg_1_engne_kit_limit)
				if (stg_1_parts_list_E)  parts_list_E  = stg_1_parts_list_E;

			if (desc.optical >= stg_2_body_kit_limit)
			{
				if (stg_2_parts_list_FL) parts_list_FL = stg_2_parts_list_FL;
				if (stg_2_parts_list_FR) parts_list_FR = stg_2_parts_list_FR;
				if (stg_2_parts_list_RL) parts_list_RL = stg_2_parts_list_RL;
				if (stg_2_parts_list_RR) parts_list_RR = stg_2_parts_list_RR;
				if (stg_2_parts_list_F)  parts_list_F  = stg_2_parts_list_F;
				if (stg_2_parts_list_Rr) parts_list_Rr = stg_2_parts_list_Rr;
				if (stg_2_parts_list_L)  parts_list_L  = stg_2_parts_list_L;
				if (stg_2_parts_list_R)  parts_list_R  = stg_2_parts_list_R;
				if (stg_2_parts_list_T)  parts_list_T  = stg_2_parts_list_T;
			}
			else
			if (desc.optical >= stg_1_body_kit_limit)
			{
				if (stg_1_parts_list_FL) parts_list_FL = stg_1_parts_list_FL;
				if (stg_1_parts_list_FR) parts_list_FR = stg_1_parts_list_FR;
				if (stg_1_parts_list_RL) parts_list_RL = stg_1_parts_list_RL;
				if (stg_1_parts_list_RR) parts_list_RR = stg_1_parts_list_RR;
				if (stg_1_parts_list_F)  parts_list_F  = stg_1_parts_list_F;
				if (stg_1_parts_list_Rr) parts_list_Rr = stg_1_parts_list_Rr;
				if (stg_1_parts_list_L)  parts_list_L  = stg_1_parts_list_L;
				if (stg_1_parts_list_R)  parts_list_R  = stg_1_parts_list_R;
				if (stg_1_parts_list_T)  parts_list_T  = stg_1_parts_list_T;
			}

		
			partLists.addElement( parts_list_E );
			partLists.addElement( parts_list_FL );
			partLists.addElement( parts_list_FR );
			partLists.addElement( parts_list_RL );
			partLists.addElement( parts_list_RR );
			partLists.addElement( parts_list_F );
			partLists.addElement( parts_list_Rr );
			partLists.addElement( parts_list_L );
			partLists.addElement( parts_list_R );
			partLists.addElement( parts_list_T );

			while( crash_count-- && partLists.size() )
			{
				float crash_badness = random(); // * desc.crash_badness_override; //
				float crash_badness_range = crash_badness;

				if (crash_badness!=0.0)
					crash_badness = crash_badness/crash_badness;

				crash_badness = clampTo(crash_badness,0.01,1.0); // from small tick to devastating crash //

				parts_list = partLists.removeElementAt( random() * partLists.size() );

				d = new Descriptor(desc);
				// because d is a clone of desc, desc is the state of the chassis, and if the chassis was hurt badly, the part will be hurt at least that badly //

				for (part_looper=0; part_looper<parts_list.length; part_looper++)
				{
					d.tear *= crash_badness+(random()*crash_badness_range-crash_badness_range/2.0);
					d.tear = clampTo(d.tear,0.0582285,1.0);
					crash_badness *= CRASH_BADNESS_DAMPING; // dampening as we get further into the car //
					d.tear = 1-d.tear;
					float r=random()*0.5;
					d.wear = desc.wear + (1.0-desc.wear)*r;

					if (random() >= d.tear*d.wear)
					{
						int t = GameLogic.CARCOLORS.length*random();
						d.color = GameLogic.CARCOLORS[t];
					}
					else
						d.color = desc.color;
					
					if(random() > desc.tear && parts_list != parts_list_E)
						continue;

					Part p = addPart( parts_list [part_looper], "???", d);

					d.tear = 1-d.tear;
				}
			}
		}

		parts_list_RGear_suspensions = stock_parts_list_RGear_suspensions;
		parts_list_RGear_shocks = stock_parts_list_RGear_shocks;
		parts_list_RGear_springs = stock_parts_list_RGear_springs;
		parts_list_RGear_brakes = stock_parts_list_RGear_brakes;
		parts_list_RGear_sways = stock_parts_list_RGear_sways;
		parts_list_RGear_wheels = stock_parts_list_RGear_wheels;
		parts_list_RGear_tyres = stock_parts_list_RGear_tyres;
		parts_list_RGear_others = stock_parts_list_RGear_others;

		if (desc.power >= stg_2_rgear_kit_limit)
		{
			if (stg_2_parts_list_RGear_suspensions)  parts_list_RGear_suspensions  = stg_2_parts_list_RGear_suspensions;
			if (stg_2_parts_list_RGear_shocks)       parts_list_RGear_shocks       = stg_2_parts_list_RGear_shocks;
			if (stg_2_parts_list_RGear_springs)      parts_list_RGear_springs      = stg_2_parts_list_RGear_springs;
			if (stg_2_parts_list_RGear_brakes)       parts_list_RGear_brakes       = stg_2_parts_list_RGear_brakes;
			if (stg_2_parts_list_RGear_sways)        parts_list_RGear_sways        = stg_2_parts_list_RGear_sways;
			if (stg_2_parts_list_RGear_wheels)       parts_list_RGear_wheels       = stg_2_parts_list_RGear_wheels;
			if (stg_2_parts_list_RGear_tyres)        parts_list_RGear_tyres        = stg_2_parts_list_RGear_tyres;
			if (stg_2_parts_list_RGear_others)       parts_list_RGear_others       = stg_2_parts_list_RGear_others;
		}
		else
		if (desc.power >= stg_1_rgear_kit_limit)
		{
			if (stg_1_parts_list_RGear_suspensions)  parts_list_RGear_suspensions  = stg_1_parts_list_RGear_suspensions;
			if (stg_1_parts_list_RGear_shocks)       parts_list_RGear_shocks       = stg_1_parts_list_RGear_shocks;
			if (stg_1_parts_list_RGear_springs)      parts_list_RGear_springs      = stg_1_parts_list_RGear_springs;
			if (stg_1_parts_list_RGear_brakes)       parts_list_RGear_brakes       = stg_1_parts_list_RGear_brakes;
			if (stg_1_parts_list_RGear_sways)        parts_list_RGear_sways        = stg_1_parts_list_RGear_sways;
			if (stg_1_parts_list_RGear_wheels)       parts_list_RGear_wheels       = stg_1_parts_list_RGear_wheels;
			if (stg_1_parts_list_RGear_tyres)        parts_list_RGear_tyres        = stg_1_parts_list_RGear_tyres;
			if (stg_1_parts_list_RGear_others)       parts_list_RGear_others       = stg_1_parts_list_RGear_others;
		}
		
		// the running gear will always be present (and flawless, sorry) - Sala //
		//partLists = new Vector();
		partLists.addElement( parts_list_RGear_suspensions );
		partLists.addElement( parts_list_RGear_shocks );
		partLists.addElement( parts_list_RGear_springs );
		partLists.addElement( parts_list_RGear_brakes );
		partLists.addElement( parts_list_RGear_sways );
		partLists.addElement( parts_list_RGear_wheels );
		partLists.addElement( parts_list_RGear_tyres );
		partLists.addElement( parts_list_RGear_others );

		d = new Descriptor(desc);

		// add any segments left that were not used in crashing //
		while( partLists.size() )
		{
			parts_list = partLists.removeElementAt(0);

			for (part_looper=0; part_looper<parts_list.length; part_looper++)
			{
				float r=random()*0.5;
				float r2=random()*0.5;
				d.tear = 1;//desc.tear + (1.0-desc.tear)*r;
				d.wear = 1;//desc.wear + (1.0-desc.wear)*r2;
				d.power = 1;
				
				if(parts_list == parts_list_RGear_wheels)
				{
					if(random() > desc.tear)
						continue;
				}
				
				addPart( parts_list[part_looper], "???", d);
			}
		}
		buildOnlyStockSuspensions = 0;
	}

	public void updateDifflock()
	{
//		setDifflock(diff_lock);
		queueEvent( null, EVENT_COMMAND, "difflock " + diff_lock  );
	}

	//---------tuning
	public int isTuneable()
	{
		return true;
	}

	// backup values //
	float old_brake_balance;
	float oldWheelXMultF;
	float oldWheelYMultF;
	float oldWheelXMultR;
	float oldWheelYMultR;
	float oldWheelRotYF;
	float oldWheelRotYR;
	float oldRimXMultF;
	float oldRimXMultR;
	float wheelPosMult = 10.0;
	float rimPosMult = 10.0;
	
	float blockPosX = 0.0;
	float blockPosY = 0.0;
	float blockPosZ = 0.0;
	float blockRotY = 0.0;
	float oldBlockPosX = 0.0;
	float oldBlockPosY = 0.0;
	float oldBlockPosZ = 0.0;
	float oldBlockRotY = 0.0;
	
	int fallBackEngineSound = 0;
	int oldfallBackEngineSound = 0;
	
	float oldFrontMaxSteer = 0.0;
	float oldRearMaxSteer = 0.0;
	float FrontMaxSteer = 1.48;
	float RearMaxSteer = 0.0;
	
	public float deg2rad( float deg )
	{
		return(deg * 3.141 / 180);
	}

	public void buildTuningMenu( Menu m )
	{
		m.y = -0.7;
		updatevariables();
		old_brake_balance = brake_balance;
		oldWheelXMultF = WheelPosXMultF;
		oldWheelYMultF = WheelPosYMultF;
		oldWheelXMultR = WheelPosXMultR;
		oldWheelYMultR = WheelPosYMultR;
		oldRimXMultF = RimPosXMultF;
		oldRimXMultR = RimPosXMultR;
		oldBlockPosX = blockPosX;
		oldBlockPosY = blockPosY;
		oldBlockPosZ = blockPosZ;
		oldBlockRotY = blockRotY;
		oldWheelRotYF = WheelRotYF;
		oldWheelRotYR = WheelRotYR;
		oldFrontMaxSteer = FrontMaxSteer;
		oldRearMaxSteer = RearMaxSteer;
		oldForcedDriveDistribution = ForcedDriveDistribution;
		oldForceFrontWheelDrive = ForceFrontWheelDrive;
		oldfallBackEngineSound = fallBackEngineSound;
		m.addItem( "F-R brake balance",		1, -brake_balance, -1.0, 0.0, 51, null ).changeVLabelText( Float.toString(brake_balance*100.0, "%1.1f %%"));
		Slider sl = m.addItem( "Wheel Pos Front X",		2, WheelPosXMultF, -0.3*wheelPosMult, 1.0*wheelPosMult, 51, null );
		sl.changeVLabelText( Float.toString(WheelPosXMultF*wheelPosMult, "%1.1f"));
		sl.setValue(WheelPosXMultF*wheelPosMult);
		sl = m.addItem( "Wheel Pos Front Y",		3, WheelPosYMultF, -1.0*wheelPosMult, 0.3*wheelPosMult, 51, null );
		sl.changeVLabelText( Float.toString(WheelPosYMultF*wheelPosMult, "%1.1f"));
		sl.setValue(WheelPosYMultF*wheelPosMult);
		sl = m.addItem( "Wheel Pos Rear X",		4, WheelPosXMultR, -0.3*wheelPosMult, 1.0*wheelPosMult, 51, null );
		sl.changeVLabelText( Float.toString(WheelPosXMultR*wheelPosMult, "%1.1f"));
		sl.setValue(WheelPosXMultR*wheelPosMult);
		sl = m.addItem( "Wheel Pos Rear Y",		5, WheelPosYMultR, -1.0*wheelPosMult, 0.3*wheelPosMult, 51, null );
		sl.changeVLabelText( Float.toString(WheelPosYMultR*wheelPosMult, "%1.1f"));
		sl.setValue(WheelPosYMultR*wheelPosMult);
		
		//sl = m.addItem( "Wheel Front Yaw", 11, WheelRotYF, -180.0, 180, 361, null );
		//sl.changeVLabelText( Float.toString(WheelRotYF, "   %1.0f degrees") );
		//sl.setValue(WheelRotYF);
		//sl = m.addItem( "Wheel Rear Yaw", 12, WheelRotYR, -180.0, 180, 361, null );
		//sl.changeVLabelText( Float.toString(WheelRotYR, "   %1.0f degrees") );
		//sl.setValue(WheelRotYR);
		
		sl = m.addItem( "Rim Pos Front X",		6, RimPosXMultF, -0.3*rimPosMult, 1.0*rimPosMult, 51, null );
		sl.changeVLabelText( Float.toString(RimPosXMultF*rimPosMult, "%1.1f"));
		sl.setValue(RimPosXMultF*rimPosMult);
		sl = m.addItem( "Rim Pos Rear X",		7, RimPosXMultR, -0.3*rimPosMult, 1.0*rimPosMult, 51, null );
		sl.changeVLabelText( Float.toString(RimPosXMultR*rimPosMult, "%1.1f"));
		sl.setValue(RimPosXMultR*rimPosMult);
		
		sl = m.addItem( "Block Pos X", 8, blockPosX, -4.0, 4.0, 1501, null );
		sl.changeVLabelText( Float.toString(blockPosX, "   %1.3f") );
		sl.setValue(blockPosX);
		sl = m.addItem( "Block Pos Y", 9, blockPosY, -4.0, 4.0, 1501, null );
		sl.changeVLabelText( Float.toString(blockPosY, "   %1.3f") );
		sl.setValue(blockPosY);
		sl = m.addItem( "Block Pos Z", 10, blockPosZ, -4.0, 4.0, 1501, null );
		sl.changeVLabelText( Float.toString(blockPosZ, "   %1.3f") );
		sl.setValue(blockPosZ);
		sl = m.addItem( "Block Yaw", 11, blockRotY, -180.0, 180, 361, null );
		sl.changeVLabelText( Float.toString(blockRotY, "   %1.0f degrees") );
		sl.setValue(blockRotY);
		
		Button b = null;
		b = m.addItem( FallbackEngineSoundIndexToName(fallBackEngineSound), 14 );
		m.addItem( ForceWheelDistribution(ForceFrontWheelDrive), 15 );
		sl = m.addItem( "Forced drive distribution", 16, ForcedDriveDistribution, 0.0, 1.0, 361, null );
		sl.changeVLabelText( Float.toString(ForcedDriveDistribution, "   %1.3f front  ") + Float.toString(1.0-ForcedDriveDistribution, "   %1.3f rear") );
		sl.setValue(ForcedDriveDistribution);
		
		sl = m.addItem( "Front max steering", 17, FrontMaxSteer, -3.14, 3.14, 361, null );
		sl.changeVLabelText( Float.toString(FrontMaxSteer, "   %1.2f") );
		sl.setValue(FrontMaxSteer);
		sl = m.addItem( "Rear max steering", 18, RearMaxSteer, -3.14, 3.14, 361, null );
		sl.changeVLabelText( Float.toString(RearMaxSteer, "   %1.2f") );
		sl.setValue(RearMaxSteer);
	}

	public void endTuningSession( int cancelled )
	{
		if( cancelled )
		{
			brake_balance = old_brake_balance;
			WheelPosXMultF = oldWheelXMultF;
			WheelPosYMultF = oldWheelYMultF;
			WheelPosXMultR = oldWheelXMultR;
			WheelPosYMultR = oldWheelYMultR;
			RimPosXMultF = oldRimXMultF;
			RimPosXMultR = oldRimXMultR;
			blockPosX = oldBlockPosX;
			blockPosY = oldBlockPosY;
			blockPosZ = oldBlockPosZ;
			blockRotY = oldBlockRotY;
			fallBackEngineSound = oldfallBackEngineSound;
			FrontMaxSteer = oldFrontMaxSteer;
			RearMaxSteer = oldRearMaxSteer;
			ForcedDriveDistribution = oldForcedDriveDistribution;
			ForceFrontWheelDrive = oldForceFrontWheelDrive;
		}
		else
		{
			GameLogic.spendTime(3*60);
		}
		updatevariables();
		forceUpdate();
		GameLogic.player.car.wakeUp();
	}

	public void handleMessage( Event m )
	{
		if( m.cmd == 1 )
		{
			brake_balance = -((Slider)m.gadget).value;
			((Slider)m.gadget).changeVLabelText( Float.toString(brake_balance*100.0, "%1.1f %%"));
		}
		else if( m.cmd == 2 )
		{
			WheelPosXMultF = ((Slider)m.gadget).value/wheelPosMult;
			((Slider)m.gadget).changeVLabelText( Float.toString(WheelPosXMultF*wheelPosMult, "%1.1f"));
		}
		else if( m.cmd == 3 )
		{
			WheelPosYMultF = ((Slider)m.gadget).value/wheelPosMult;
			((Slider)m.gadget).changeVLabelText( Float.toString(WheelPosYMultF*wheelPosMult, "%1.1f"));
		}
		else if( m.cmd == 4 )
		{
			WheelPosXMultR = ((Slider)m.gadget).value/wheelPosMult;
			((Slider)m.gadget).changeVLabelText( Float.toString(WheelPosXMultR*wheelPosMult, "%1.1f"));
		}
		else if( m.cmd == 5 )
		{
			WheelPosYMultR = ((Slider)m.gadget).value/wheelPosMult;
			((Slider)m.gadget).changeVLabelText( Float.toString(WheelPosYMultR*wheelPosMult, "%1.1f"));
		}
		else if( m.cmd == 6 )
		{
			RimPosXMultF = ((Slider)m.gadget).value/rimPosMult;
			((Slider)m.gadget).changeVLabelText( Float.toString(RimPosXMultF*rimPosMult, "%1.1f"));
		}
		else if( m.cmd == 7 )
		{
			RimPosXMultR = ((Slider)m.gadget).value/rimPosMult;
			((Slider)m.gadget).changeVLabelText( Float.toString(RimPosXMultR*rimPosMult, "%1.1f"));
		}
		else if( m.cmd == 8 )
		{
			blockPosX = ((Slider)m.gadget).value;
			((Slider)m.gadget).changeVLabelText( Float.toString(blockPosX, "%1.1f"));
		}
		else if( m.cmd == 9 )
		{
			blockPosY = ((Slider)m.gadget).value;
			((Slider)m.gadget).changeVLabelText( Float.toString(blockPosY, "%1.1f"));
		}
		else if( m.cmd == 10 )
		{
			blockPosZ = ((Slider)m.gadget).value;
			((Slider)m.gadget).changeVLabelText( Float.toString(blockPosZ, "%1.1f"));
		}
		else if( m.cmd == 11 )
		{
			blockRotY = ((Slider)m.gadget).value;
			((Slider)m.gadget).changeVLabelText( Float.toString(blockRotY, "   %1.0f degrees") );
		}
		else if( m.cmd == 12 )
		{
			WheelRotYF = ((Slider)m.gadget).value;
			((Slider)m.gadget).changeVLabelText( Float.toString(WheelRotYF, "   %1.0f degrees") );
		}
		else if( m.cmd == 13 )
		{
			WheelRotYR = ((Slider)m.gadget).value;
			((Slider)m.gadget).changeVLabelText( Float.toString(WheelRotYR, "   %1.0f degrees") );
		}
		else if( m.cmd == 14 )
		{
			fallBackEngineSound = (fallBackEngineSound + 1) % SoundOverWriteCount;
			(m.gadget).changeLabelText( FallbackEngineSoundIndexToName(fallBackEngineSound) );
		}
		else if( m.cmd == 15 )
		{
			ForceFrontWheelDrive = 1-ForceFrontWheelDrive;
			(m.gadget).changeLabelText( ForceWheelDistribution(ForceFrontWheelDrive) );
		}
		else if( m.cmd == 16 )
		{
			ForcedDriveDistribution = ((Slider)m.gadget).value;
			((Slider)m.gadget).changeVLabelText( Float.toString(ForcedDriveDistribution, "   %1.3f front  ") + Float.toString(1.0-ForcedDriveDistribution, "   %1.3f rear") );
		}
		else if( m.cmd == 17 )
		{
			FrontMaxSteer = ((Slider)m.gadget).value;
			((Slider)m.gadget).changeVLabelText( Float.toString(FrontMaxSteer, "   %1.2f"));
		}
		else if( m.cmd == 18 )
		{
			RearMaxSteer = ((Slider)m.gadget).value;
			((Slider)m.gadget).changeVLabelText( Float.toString(RearMaxSteer, "   %1.2f"));
		}
		if(m.cmd == 8 || m.cmd == 9 || m.cmd == 10 || m.cmd == 11)
		{
			UpdateOnlyBlock();
		}
		if(m.cmd == 2 || m.cmd == 3 || m.cmd == 4 || m.cmd == 5 || m.cmd == 6 || m.cmd == 7 || m.cmd == 11 || m.cmd == 12)
		{
			if(GameLogic.player != null && GameLogic.player.car != null)
			{
				UpdateOnlyWheels();
				forceUpdate();
				GameLogic.player.car.wakeUp();
			}
		}
	}
	public float WheelPosXMultF = 0.0;
	public float WheelPosYMultF = 0.0;
	public float WheelPosXMultR = 0.0;
	public float WheelPosYMultR = 0.0;
	public float WheelRotYF = 0.0;
	public float WheelRotYR = 0.0;
	public float RimPosXMultF = 0.0;
	public float RimPosXMultFMinOffset = 0.12;
	public float RimPosXMultR = 0.0;
	public float RimPosXMultRMinOffset = 0.02;
	void ShockAbsorberForBigfoot(int wheel_i)
	{
		Part other = partOnSlot(301+wheel_i);
		if (other && other instanceof ShockAbsorber)
		{
			ShockAbsorber sa = other;
			sa.damping = 3000.0;
		}
		if (other && other instanceof Pneumatic_ShockAbsorber)
		{
			Pneumatic_ShockAbsorber sa = other;
			sa.damping = 3000.0;
		}
	}
	public void PlaceRollPreventionBoxes()
	{
		for(int i=0; i<wheels; i++)
		{
			int rollPreventionMissingCount = 0;
			Part other = partOnSlot(5024);
			if(other == null)
				rollPreventionMissingCount++;
			other = partOnSlot(5025);
			if(other == null)
				rollPreventionMissingCount++;
			other = partOnSlot(5026);
			if(other == null)
				rollPreventionMissingCount++;
			other = partOnSlot(5027);
			if(other == null)
				rollPreventionMissingCount++;
			for(int roll_i = 0; roll_i!=rollPreventionMissingCount;roll_i++)
			{
				addPart( cars.racers:0x0000201Er, "Rollprevention",0, 1000.0, 1000.0  );
			}
			Vector3 wheelPos = getWheelPos(i);
			wheelPos.y -= 0.2;
			other = partOnSlot(101+i);
			int wheelID = 0;
			if (other && other instanceof Wheel)
			{
				wheelID = other.getInfo( GII_TYPE );
				if(wheelID == parts.amilmandfinalmerge:0x190r || wheelID == parts.amilmandfinalmerge:0x18Cr)
				{
					wheelPos.y -= 0.3;
					wheelPos.x *= 1.15;
					ShockAbsorberForBigfoot(i);
				}
			}
			setSlotPos( 5024+i, wheelPos, null );
		}
	}
	public void UpdateOnlyWheels()
	{
		//super.updatevariables();
		for(int i=0; i<wheels; i++)
		{
			WheelRef	whl = getWheel(i);
			if(whl != null)
			{
				float currentSlotX = 0.0;
				Vector3 pos = whl.getPos();
				Ypr rot = new Ypr();
				pos.z = 0;
				if(i == 0 || i == 1)
				{
					if(pos.x < 0)
					{
						pos.x = -WheelPosXMultF;
						rot.r = -(WheelRotYF);
						currentSlotX = -(RimPosXMultF+RimPosXMultFMinOffset);
						RawEdit.setF(whl.ptr+0xc8,FrontMaxSteer);
					}
					else
					{
						pos.x = WheelPosXMultF;
						rot.r = (WheelRotYF);
						currentSlotX = (RimPosXMultF+RimPosXMultFMinOffset);
						RawEdit.setF(whl.ptr+0xc8,FrontMaxSteer);
					}
					pos.y = WheelPosYMultF;
				}
				else
				{
					if(pos.x < 0)
					{
						pos.x = -WheelPosXMultR;
						rot.r = -(WheelRotYR);
						currentSlotX = -(RimPosXMultR+RimPosXMultRMinOffset);
						RawEdit.setF(whl.ptr+0xc8,RearMaxSteer);
					}
					else
					{
						pos.x = WheelPosXMultR;
						rot.r = (WheelRotYR);
						currentSlotX = (RimPosXMultR+RimPosXMultRMinOffset);
						RawEdit.setF(whl.ptr+0xc8,RearMaxSteer);
					}
					pos.y = WheelPosYMultR;
				}
				whl.setPos(pos);
				//whl.setYpr(rot);
				//UpdateCamber(i);
				setSlotPos( 101+i, new Vector3(currentSlotX, 0, 0), null );
			}
		}
		PlaceRollPreventionBoxes();
	}
	void UpdateCamber(int wheel_i)
	{
		WheelRef wheel = getWheel(wheel_i);
		Suspension sus = partOnSlot(121+wheel_i);
		float side = 1.0;
		sus.camber = WheelRotYR;
		if(wheel_i == 0 || wheel_i == 1)
			sus.camber = WheelRotYF;
		if (wheel != null && sus != null)
		{
			Vector3 pos = wheel.getPos();
			if(pos.x < 0)
				side = -1.0;
			wheel.setArm( 0.296,
										0.294, 0.030, 0.000,
										0.000, 0.000, 1.000);
			wheel.setHub( 0.000,
					-0.077,-0.043,-0.004,
					0.071+deg2rad(side*sus.camber), 0.407, 0.000,
					0.071, 0.407, 0.000);

			sus.VICx = -0.616;
			sus.VICy = -0.235;

			sus.LICy = -0.150;
			sus.LICz =  0.250;
			sus.setRollCenter(wheel);
			sus.setSuspensionGeometry(wheel);
			sus.updatevariables();
		}
	}
	public void UpdateOnlyBlock()
	{
		if(blockPosX != 0 || blockPosY != 0 || blockPosZ != 0 || blockRotY != 0)
		{
			setSlotPos( 401, new Vector3(blockPosX,blockPosY,blockPosZ), new Ypr(deg2rad(blockRotY),0,0) );
		}
	}
	int SoundOverWriteCount = 51;//last case +1
	String FallbackEngineSoundIndexToName(int ind)
	{
	}
	String ForceWheelDistribution(int ind)
	{
		if(ind == 0)
			return "Wheel drive is not forced";
		return "Wheel drive is forced";
	}
	int oldForceFrontWheelDrive = 0;
	float oldForcedDriveDistribution = 1.0;
	public int ForceFrontWheelDrive = 0;
	public float ForcedDriveDistribution = 1.0;
	public void SetFrontWheelDrive()
	{
		if(ForceFrontWheelDrive == 1)
		{
			drive_type = 0;
			Block engine = null;
			Part part = null;
			int slotIndex;
			int slotID;
			int slots = getSlots();
			for( slotIndex=0; slotIndex<slots; slotIndex++ )
			{
				slotID = getSlotID( slotIndex );
				part = partOnSlot(slotID);

				if (part)
				{
					if (part instanceof Block)
					{
						engine = part;
						Transmission trans = engine.getTransmission();
						if(trans)
						{
							trans.drive_type = 3;
							float valFour = ForcedDriveDistribution;
							trans.drive_front = valFour;
							trans.drive_front_max = 1.0;
							trans.drive_front_min = 0.0;
							trans.def_drive_front = valFour;
							break;
						}
					}
				}
			}
		}
	}
	public void repair()
	{
		super.repair();
		updatevariables();
	}
	//---------tuning

	//public int getCarID(){ return carID; }
	public void SetSFX_trans_fwd(int TransFwd) { SFX_trans_fwd = TransFwd; }
	public void SetSFX_trans_rev(int TransRev) { SFX_trans_rev = TransRev; }
	public void SetSFX_ignition(int Ignition) { SFX_ignition = Ignition; }
	public void Setrpm_trans_fwd(float rpm) { rpm_trans_fwd = rpm; }
	public void Setrpm_trans_rev(float rpm) { rpm_trans_rev = rpm; }
	public void Setsfx_starter_rpm(float rpm) { sfx_starter_rpm = rpm; }
	
	public native float getTorque( float RPM, float boost );
	public native float getMass( );
	public native Vector3 getCM( );
	public native Vector3 getMin( );
	public native Vector3 getMax( );
	public native Vector3 getWheelPos( int n );
	public native void forceUpdate( );
	public native String getWheelDamage( int index );
	public native void setWheelDamage( int index, String data );
	public native void setCooling( float min, float max, float spd ); // default: 10, 50, 0.01 ; spd az negyzetes!!!! - Sala //

	public native SfxTable getSfxTable( int id );
	public native void setSfxExhaustMinVol(float f);
	public native void setSteerWheelRadius(float f);
	public native void setSteerWheel(float r, float z);
	public native void setHornSFX( ResourceRef sfx, float pitch, int index );
	public native void setNitroSFX( ResourceRef sfx, float pitch );

	public native float getMileage(	);
	public native void setMileage( float m );

	public native void setBuck( int partID, int buckid, float freq, float prob, float rpmdep, float amp );

	public native int getWheels();
	public native WheelRef getWheel( int id );
	public static final int IndexedData_Mileage = 0x20FC;
	public static final int IndexedData_RPM = 0x20FC-0x1B0;
	public static final int IndexedData_GearStrength = 0x20FC-0x324;
	public static final int IndexedData_CarRuntime = 0x20FC-0x2D0;
	public static final int IndexedData_ExhaustActivity= 0x20FC-0x2D4;//related to turbo but returns values even when there is no turbo installed
	public static final int IndexedData_CurrentSteering1 = 0x20FC-0x350;
	public static final int IndexedData_CurrentSteering2 = 0x20FC-0x34C;
	public static final int IndexedData_CurrentSteering3 = 0x20FC-0x1EC;
	public static final int IndexedData_Handbreak = 0x20FC-0x33C;
	public static final int IndexedData_BreakStrength = 0x20FC-0x344;
	public static final int IndexedData_ThrottleStrength = 0x20FC-0x348;
	public static final int IndexedData_ClutchStrength = 0x20FC-0x340;
	public static final int IndexedData_NitroStrength = 0x20FC-0x338;
	public static final int IndexedData_NOSLevel = 0x20FC-0x204;
	public static final int IndexedData_EngineWear = 0x20FC-0x2C4;
	public static final int IndexedData_HandbreakLED = 0x20FC-0x1D30; //return 0.0 if it is not lit returns something else if it is
	public static final int IndexedData_OverDriveLED = 0x20FC-0x1D20; //return 0.0 if it is not lit returns something else if it is
	public static final int IndexedData_ASRLED = 0x20FC-0x1D40;       //return 0.0 if it is not lit returns something else if it is
	public static final int IndexedData_ABSLED = 0x20FC-0x1D50;       //return 0.0 if it is not lit returns something else if it is
	//index is an offset really it should not be 0 and I do not know how high it can be
	//calls like: ....car.chassis.getIndexedData(Chassis.IndexedData_ThrottleStrength);
	public native float getIndexedData( int index );
	public native int getIndexedAddr( int index );
	public static final int IndexedData_GearNumber = 0x20FC-0x304;//this is an int getIndexedData will return strange but the RawEdit.getI with getIndexedAddr will suffice
	//democall in garage: RawEdit.setI(player.car.chassis.getIndexedAddr(Chassis.IndexedData_GearNumber),4);
	public static final int IndexedData_Flags = 0x70;
	public static final int IndexedData_SpeedLimit = 0x2104;
	//known flags:
	//0x1000 cruise mode
	public float GetCurrentTurboValue()
	{
		if(dynodata == null)
			return 0;
		float v2 = 0.0;
		float v3 = 0.0;
		float v4 = 0.0;
		float v5 = 0.0;
		float v6 = 0.0;
	
		float CurrentRPM = getIndexedData( IndexedData_RPM );
		
		if ( dynodata.rpm_turbo_opt <= 0.0 || dynodata.rpm_turbo_range <= 0.0 || dynodata.P_turbo_max <= 0.0 || dynodata.P_turbo_waste <= 0.0 )
			return 0.0;
		v2 = (CurrentRPM * dynodata.rpm_turbo_mul - dynodata.rpm_turbo_opt);
		if(v2 < 0)
			v2 *= -1.0;
		if ( v2 >= 0 )
		{
			v3 = 1.0 - (v2) / (dynodata.rpm_turbo_range);
			if ( v3 < 0.0 )
				v3 = 0.0;
		}
		else
		{
			v3 = 1.0;
		}
		v4 = v3 * dynodata.P_turbo_max;
		if ( v4 >= dynodata.P_turbo_waste )
			v4 = dynodata.P_turbo_waste;
		v5 = Math.sqrt(v4 + 1.0);
		if ( dynodata.P_turbo_waste <= dynodata.P_turbo_max )
			v6 = dynodata.P_turbo_waste;
		else
			v6 = dynodata.P_turbo_max;
		v6 = Math.sqrt(v6);
		if(v6 == 1.0)
		{
			if(v5 - 1.0 > 0.0001)
				return 999999;
			else
				return -999999;
		}
		return (v5 - 1.0) / (v6 - 1.0);//can return + or - inf or NAN
	}
}
