package java.game.parts.running_gear.sways;

import java.game.parts.rgearpart.*;


public class swaybar_Prime_rear extends Swaybar
{
	public swaybar_Prime_rear( int id )
	{
		super( id );

		force = 25000.0;
		damping = 2500.0;

		name_prefix = "Prime DLH 500 rear";
		brand_prestige_factor = 3.25;
		calcStuffs();
	}
}

