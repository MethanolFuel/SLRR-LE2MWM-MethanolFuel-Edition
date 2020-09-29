package java.game.parts.running_gear.sways;

import java.game.parts.rgearpart.*;

public class swaybar_Racing_front extends Swaybar
{
	public swaybar_Racing_front( int id )
	{
		super( id );

		force = 40000.0;
		damping = 4000.0;
		adjustable = 1;
		min_force = 50000.0;
		max_force = 35000.0;

		name_prefix = "Racing front";
		brand_prestige_factor = 2.00;
		calcStuffs();
	}
}