package java.game.parts.CMS_Brakes;

import java.game.parts.rgearpart.reciprocatingrgearpart.brake.*;

public class brake_disk_370_271_1_L extends Disc
{
	public brake_disk_370_271_1_L( int id )
	{
		super( id );
		
		diam_mm	= 370.0;
		friction_disc		= RT_VENTEDGROOVED;
		force			= CL_RACEFORCE;
		friction_pad		= 1.05;
		number_of_calipers	= 3.0;
		calcStuffs();
		name_prefix = "Brembo racing disc 370 mm";
		brand_prestige_factor = 1.75;


		renderID_FL = parts.CMS_Brakes:0x00000D34r;
		renderID_FR = parts.CMS_Brakes:0x00000D35r;
		renderID_RL = parts.CMS_Brakes:0x00000D34r;
		renderID_RR = parts.CMS_Brakes:0x00000D35r;
	}
}
