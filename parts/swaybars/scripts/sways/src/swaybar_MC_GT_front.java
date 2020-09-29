package java.game.parts.running_gear.sways;

import java.game.parts.rgearpart.*;


public class swaybar_MC_GT_front extends Swaybar
{
	public swaybar_MC_GT_front( int id )
	{
		super( id );

		force = 25000.0;
		damping = 2500.0;

		name_prefix = "MC GT front";
		brand_prestige_factor = 2.00;
		calcStuffs();
	}
}

