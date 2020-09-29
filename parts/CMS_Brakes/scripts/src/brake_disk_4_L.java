package java.game.parts.CMS_Brakes;

import java.game.parts.rgearpart.reciprocatingrgearpart.brake.*;

public class brake_disk_4_L extends Disc
{
	public brake_disk_4_L( int id )
	{
		super( id );

		diam_mm	= 358.0;
		friction_disc		= RT_VENTEDGROOVED;
		force			= CL_FORCE_BRUTAL;
		friction_pad		= 1.1;
		number_of_calipers	= 3.0;
		calcStuffs();
		name = "Endless disc 358 mm";
		brand_prestige_factor = 1.75;

		
		renderID_FL = parts.CMS_Brakes:0x00000D51r;
		renderID_FR = parts.CMS_Brakes:0x00000D52r;
		renderID_RL = parts.CMS_Brakes:0x00000D51r;
		renderID_RR = parts.CMS_Brakes:0x00000D52r;
	}
}