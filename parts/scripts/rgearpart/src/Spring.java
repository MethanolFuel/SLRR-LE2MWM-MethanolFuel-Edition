package java.game.parts.rgearpart;

import java.render.osd.*;
import java.io.*;
import java.util.*;
import java.util.resource.*;
import java.game.cars.*;
import java.game.parts.*;
import java.game.parts.bodypart.*;
import java.game.parts.enginepart.*;

public class Spring extends RGearPart
{
	float	force	= 20000.0;// N/m
	float	damping	= 2000.0;//calculation of damping is at line 38	// N/(m/s)
	float	restlength = 0.45;// 0.39 old	// m
	float	maxlength = 0.7;//0.3	// m
	float	minlength = 0.1;//0.1	// m
	float	designedMassOnWheel = 0;//300.0;
	
	float 	length_mul = 1.05;
	float 	force_mul = 0.75;
	float	minlenth_mul = 0.7;

	final static int	WHL_FRONT = 0;
	final static int	WHL_REAR = 1;

	public Spring( int id )
	{
		super( id );

		name = "Spring";

		prestige_calc_weight = 20.0;
	}

	public void calcStuffs()
	{
		value = tHUF2USD(brand_prestige_factor * (force/4000.0 + 0.100/(maxlength - minlength)*10.0));
		brand_new_prestige_value = value / 3.0;
		damping = force * 0.001;// changed muliplier from 0.15 to 0.05

		name = name_prefix + " " + Float.toString(force,"%1.0f N/m")+" "+Float.toString(restlength*100.0/2.54, "%1.1f\"")+" spring";
		description = "It's a "+name+". It can be span up to "+Float.toString(maxlength*100.0/2.54, "%1.1f\"")+" and be compressed to "+Float.toString(minlength*100.0/2.54, "%1.1f\"")+".";
		restlength = PositionedNPm2RestLength(designedMassOnWheel, force, restlength);
	}

	public float getMessOnWheel(float totalMass, float frontLoad, int whichEnd)
	{
		if (whichEnd == WHL_FRONT)
			return totalMass*frontLoad/200.0;
		return totalMass*(100.0-frontLoad)/200.0;
	}

	public float GetSpringRate(float rl, float rl2, float load)
	{
		return load/(rl-rl2);
	}

	public float kg2N(float k)
	{
		return k*9.81;
	}

	public float kgfPmm2NPm(float kgfPmm)
	{
		return kgfPmm*1000.0*9.81;
	}

	public float PositionedNPm2RestLength(float massPerWheel, float NPm, float rl)
	{
/*		System.log(name+"->PositionedNPm2RestLength:");
		System.log("  massPerWheel = "+massPerWheel);
		System.log("  NPm          = "+NPm);
		System.log("  rl           = "+rl);*/

		float f1 = massPerWheel*9.81; // equals the force needed for 1m stroke

		rl = rl+f1/NPm;

//		System.log(" result: rl    = "+rl);

		return rl;
	}

	public void updatevariables()
	{
		WheelRef whl = getWheel();

		if (whl)
		{
			whl.setDamping( damping , damping );
			whl.setForce(force*force_mul);
			whl.setRestLen(restlength*length_mul);
			whl.setMinLen(minlength*minlenth_mul);
			if (restlength>maxlength)
				whl.setMaxLen(restlength*length_mul);
			else
				whl.setMaxLen(maxlength*length_mul);

/*			System.log("  damping (spring)        = " + damping);
			System.log("  force                   = " + force );
			System.log("  restlength              = " + restlength );
			System.log("  minlength               = " + minlength );
			System.log("  maxlength (uncorrected) = " + maxlength );*/
		}
	}
}
