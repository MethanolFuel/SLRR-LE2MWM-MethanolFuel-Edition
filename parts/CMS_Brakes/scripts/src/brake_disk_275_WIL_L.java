package java.game.parts.CMS_Brakes;

import java.game.parts.rgearpart.reciprocatingrgearpart.brake.*;

public class brake_disk_275_WIL_L extends Disc
{
	public brake_disk_275_WIL_L( int id )
	{
		super( id );
		
		diam_mm	= 275.0;
		friction_disc		= RT_VENTEDGROOVED;
		force			= CL_FORCE_1;
		friction_pad		= 1.0;
		number_of_calipers	= 2.0;
		calcStuffs();
		name = "Wilwood disc 275 mm";
		brand_prestige_factor = 1.75;


		renderID_FL = parts.CMS_Brakes:0x00000D45r;
		renderID_FR = parts.CMS_Brakes:0x00000D46r;
		renderID_RL = parts.CMS_Brakes:0x00000D45r;
		renderID_RR = parts.CMS_Brakes:0x00000D46r;
	}
}
