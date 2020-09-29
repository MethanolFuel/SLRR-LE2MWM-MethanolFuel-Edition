package java.game.parts.CMS_Brakes;

import java.game.parts.rgearpart.reciprocatingrgearpart.brake.*;

public class brake_disk_380_274_C_L extends Disc
{	
	public brake_disk_380_274_C_L( int id )
	{
		super( id );
		
		diam_mm	= 380.0;
		friction_disc		= RT_CARBON;
		force			= CL_RACEFORCE;
		friction_pad		= 1.1;
		number_of_calipers	= 3.0;
		calcStuffs();
		name = "Brembo carbon disc 380 mm";
		brand_prestige_factor = 1.75;

		
		renderID_FL = parts.CMS_Brakes:0x00000D14r;
		renderID_FR = parts.CMS_Brakes:0x00000D15r;
		renderID_RL = parts.CMS_Brakes:0x00000D14r;
		renderID_RR = parts.CMS_Brakes:0x00000D15r;
	}
}
