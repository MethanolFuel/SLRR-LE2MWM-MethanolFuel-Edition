package java.game.parts.running_gear.sways;

import java.game.parts.rgearpart.*;


public class swaybar_MC_GTB_front extends Swaybar
{
	public swaybar_MC_GTB_front( int id )
	{
		super( id );

		force = 25000.0;
		damping = 2500.0;

		name_prefix = "MC GT-B front";
		brand_prestige_factor = 1.90;
		calcStuffs();
	}
}

