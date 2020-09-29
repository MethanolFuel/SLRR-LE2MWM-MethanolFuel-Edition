package java.game.parts.bodypart;

import java.io.*;
import java.util.*;
import java.util.resource.*;
import java.render.osd.*;
import java.game.*;
import java.game.cars.*;
import java.game.parts.*;
import java.game.parts.bodypart.*;
import java.game.parts.enginepart.*;
import java.game.parts.rgearpart.*;
import java.game.parts.rgearpart.reciprocatingrgearpart.*;

public class HeadlightController extends BodyPart
{
	// HeadlightController script by RedCarDriver. Version 2018-11-19.
	
	int hasExhaustiveBits = 0;
	
	float degs = 0;
	float rads = 0;
	float fullcloserads = 0;
	float fullopenrads = 30;
	float openrads = 0;
	float closerads = 0;

	float open_Time = 21.0*3600; //time of day in seconds when headlights will open
	float close_Time = 6.0*3600; //time of day in seconds when headlights will close
    
//	Vector3 opened_pos = new Vector3( 0.000, 0.040, 0.020 ); // opened position
	float animate_Speed = 200.0; // speed of opening

	int debug_show_opened = 0; // type 1 to tunne opened position
	int car_is_on = 0; // for Amilmand stuff only
	
	float current_pos_rads;
	float fullradsweep, radsweep;

	Thread AnimateThread;
	float animate_last_time, animate_time_delta;
	int installed;

	// Open modes:
	// 0 = normal operation
	// 1 = full close, custom angle open
	// 2 = custom angle open, full close
	// 3 = custom angle full-time

	int open_mode = 0;
	
	// LEGACY STUFF FOR OLD MODS
	float	animate_progress, animate_last_progress; // legacy/deprecated - no longer used, but retained for old versions
	Ypr anim_ori; // rotation vector; doesn't do anything in this version, but left in for legacy compatibility

	public HeadlightController( int id )
	{
		super( id );
		carCategory = PACKAGE;
		name = "Headlight Controller";
		description = "Controls headlight motions. Class created by RedCarDriver. Pop-up headlight script by Svander. Auto-movement scripting by Miran and Franco.";
		
		rads = fullopenrads;
		degs = rad2deg(rads);
		fullradsweep = fullopenrads-fullcloserads;

		updatelightslotpos();
		fixOpenVariables();
		hasExhaustiveBits = checkExhaustiveBits();

		AnimateThread = new Thread( this, "Animate Thread" );
		animate_last_time = System.simTime();
		AnimateThread.start();
	}
	
	public void fixOpenVariables()
	{
		if ( open_mode == 3 ) // Always custom angle
		{
			openrads = rads;
			closerads = rads;
		}
		else
		if (open_mode == 1) // Fully closed, custom angle when open
		{
			openrads = rads;
			closerads = fullcloserads;
		}
		else
		if (open_mode == 2) // Custom angle when closed, fully open
		{
			openrads = fullopenrads;
			closerads = rads;
		}
		else // Full open and close
		{
			openrads = fullopenrads;
			closerads = fullcloserads;
		}
		radsweep = openrads-closerads;
	}
	
	public void updatevariables()
	{
		super.updatevariables();
	}
	
	public int checkExhaustiveBits()
	{
		return File.exists("system\\Scripts\\lang\\RawEdit.class");
	}
	
	public void load( File saveGame )
	{
		super.load( saveGame );

		int	save_ver = saveGame.readInt();

		if (save_ver >= 3)
		{
			rads = saveGame.readFloat();
			degs = rad2deg( rads );
			open_mode = saveGame.readInt();
			fixOpenVariables();
		}
	}

	// This class does NOT provide for actually moving the headlights to their final position, as that depends on the individual slot setup.
	// You will need to write a little bit of code to actually move the headlights.
	// Suggested implementation:
	//{
	//	setSlotPos( 7, null, new Ypr(0, current_pos_rads, 0));
	//	setSlotPos( 10, null, new Ypr(0, current_pos_rads, 0));
	//}
	public void updatelightslotpos()
	{
	}
	
	// Method for resetting headlight position as an anti-bug measure. Again, you will need to supply this yourself.
	public void resetlightslotpos()
	{
	}

	public void debugopen() // Makes headlights show up as opened temporarily.
	{
		debug_show_opened = 1;
	}

	public void resetAnim() // forces animation to reset
	{
		debug_show_opened = 0;
		resetlightslotpos();
	}

	// Wrote a bunch of accessor and mutator functions because, strictly speaking, it's
	// better coding practice than just modifying them because they're public.

	public float getDegs()
	{
		return this.degs;
	}
	public float getRads()
	{
		return this.rads;
	}
	public float getFullCloseRads()
	{
		return this.fullcloserads;
	}
	public float getFullOpenRads()
	{
		return this.fullopenrads;
	}
	public float getCloseRads()
	{
		return this.closerads;
	}
	public float getOpenRads()
	{
		return this.openrads;
	}
	public int getOpenMode()
	{
		return this.open_mode;
	}
	public void setDegs(float newDegs)
	{
		this.degs = newDegs;
	}
	public void setRads(float newRads)
	{
		this.rads = newRads;
	}
	public void setOpenMode(int newOpenMode)
	{
		this.open_mode = newOpenMode;
	}

	public void save( File saveGame )
	{
		super.save( saveGame );

		int	save_ver = 4;

		saveGame.write( save_ver );
		if (save_ver >= 3)
		{
			saveGame.write( rads );
			saveGame.write( open_mode );
		}
	}
	
	public float rad2deg( float rad )
	{
		return(rad * 180 / 3.14159);
	}
	public float deg2rad( float deg )
	{
		return(deg * 3.14159 / 180);
	}
	
	public void run()
	{
		while( 1 )
		{
			if(AnimateThread)
			{
				animate_time_delta = System.simTime()-animate_last_time;
				the_car = null;
				getCar_LocalVersion();

				//if( the_car && GameLogic.player && GameLogic.player.car && GameLogic.player.car.chassis && the_car == GameLogic.player.car.chassis ) // player is using this car and custom angle demonstration is not applied
				if( the_car ) // make sure this is attached to a car at all
				{
					installed = 1;
					fixOpenVariables();
					if(hasExhaustiveBits)
					{
						if(the_car.getIndexedData(Chassis.IndexedData_RPM) > 200)
							car_is_on = 1;
						else
							car_is_on = 0;
					}
					else
						car_is_on = 1;
					
					if(debug_show_opened)
						current_pos_rads = rads;
					else
					{
						if( ((GameLogic.time > this.open_Time || GameLogic.time < this.close_Time) && car_is_on) || debug_show_opened )
						{
							if( current_pos_rads < openrads )
								current_pos_rads += animate_Speed*0.01*animate_time_delta*radsweep;
						}
						else
						{
							if( current_pos_rads > closerads )
								current_pos_rads -= animate_Speed*0.01*animate_time_delta*radsweep;
						}
					
						// clamp position
						if( current_pos_rads > openrads ) current_pos_rads = openrads;
						if( current_pos_rads < closerads ) current_pos_rads = closerads;
					}

					updatelightslotpos();
					
					animate_last_time = System.simTime();
					if(AnimateThread) AnimateThread.sleep(35); // Wait 0.035 sec (in theory will 29 FPS)
				}
				else
				{
					if( installed )
					{
						resetlightslotpos();
						//animate_progress = 0;
						installed = 0;
					}

					if(AnimateThread) AnimateThread.sleep( 2000 ); // Wait 2 sec
				}
			}
		}
	}
}
