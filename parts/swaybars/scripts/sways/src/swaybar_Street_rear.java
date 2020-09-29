package java.game.parts.running_gear.sways;

import java.game.parts.rgearpart.*;

public class swaybar_Street_rear extends Swaybar
{
	public swaybar_Street_rear( int id )
	{
		super( id );

		force = 25000.0;
		damping = 2500.0;
		adjustable = 1;
		min_force = 30000.0;
		max_force = 20000.0;
		
		name_prefix = "Street rear";
		brand_prestige_factor = 1.20;
		calcStuffs();
	}
}

