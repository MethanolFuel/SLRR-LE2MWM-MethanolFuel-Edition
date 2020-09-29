package java.game.parts.running_gear.sways;

import java.game.parts.rgearpart.*;


public class swaybar_Ishima_WRS_rear extends Swaybar
{
	public swaybar_Ishima_WRS_rear( int id )
	{
		super( id );

		force = 25000.0;
		damping = 2500.0;

		name_prefix = "Ishima Enula WR SuperTurizmo rear";
		brand_prestige_factor = 3.30;
		calcStuffs();
	}
}

