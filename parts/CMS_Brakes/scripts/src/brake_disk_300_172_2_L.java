package java.game.parts.CMS_Brakes;

import java.game.parts.rgearpart.reciprocatingrgearpart.brake.*;

public class brake_disk_300_172_2_L extends Disc
{
	public brake_disk_300_172_2_L( int id )
	{
		super( id );
		
		diam_mm	= 300.0;
		friction_disc		= RT_VENTEDGROOVED;
		force			= CL_RACEFORCE;
		friction_pad		= 1.1;
		number_of_calipers	= 2.0;
		calcStuffs();
		name = "Brembo racing disc 300 mm";
		brand_prestige_factor = 1.75;


		renderID_FL = parts.CMS_Brakes:0x00000D10r;
		renderID_FR = parts.CMS_Brakes:0x00000D12r;
		renderID_RL = parts.CMS_Brakes:0x00000D10r;
		renderID_RR = parts.CMS_Brakes:0x00000D12r;
	}
}
