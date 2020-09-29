package java.game.parts.rgearpart;

import java.render.osd.*;
import java.io.*;
import java.util.*;
import java.util.resource.*;
import java.game.*;
import java.game.cars.*;
import java.game.parts.*;
import java.game.parts.bodypart.*;
import java.game.parts.enginepart.*;
import java.game.parts.rgearpart.*;

public class Pneumatic_ShockAbsorber extends RGearPart
{
	int	type, type1, type2, type3; 
	int			mode = 0, mode2 = 0;
	String[]	TypeText;

	final static SfxRef  Use_Up_SFX = new SfxRef( MW_Mod:0x0072r );
	final static SfxRef  Use_Down_SFX = new SfxRef( MW_Mod:0x0071r );

	float	damping				= 5000.0;// N/(m/s)
	float	max_damping			= 6000.0;// N/(m/s)
	float	min_damping			= 3000.0;// N/(m/s)
	
	float	rebound_factor		= 0.2;  // GAS

	float	min_length			= 0.10;	// m
	float	max_length			= 0.40;	// m

	float	Up_length = 0.20; // m
	float	Down_length = 0.30; // m
	float	Cur_length= 0.20; // m

	WheelRef whl;

	public Pneumatic_ShockAbsorber ( int id )
	{
		super( id );

		TypeText = new String[4];
		TypeText[0] = "FRONT LEFT";		
		TypeText[1] = "FRONT RIGHT";		
		TypeText[2] = "REAR LEFT";		
		TypeText[3] = "REAR RIGHT";	
	}

	public void load( File saveGame )
	{
		super.load( saveGame );

		damping = saveGame.readFloat();
		type = saveGame.readInt();
		Up_length = saveGame.readFloat();
		Down_length = saveGame.readFloat();
		Cur_length = Up_length;
	}

	public void save( File saveGame )
	{
		super.save( saveGame );

		saveGame.write( damping );
		saveGame.write( type );
		saveGame.write( Up_length );
		saveGame.write( Down_length );
	}

	public void install()
	{
		getCar_LocalVersion();
		if(the_car)
			the_car.PneumaticSA.addElement(this);

		mode2 = 0;

		if(type == 0) // FL
		{
			type1 = 1; // num 7
			type2 = 2; // num 8
			type3 = 4; // num 4
		}
		else
		if(type == 1) // FR
		{
			type1 = 3; // num 9
			type2 = 2; // num 8
			type3 = 6; // num 6
		}
		else
		if(type == 2) // RL
		{
			type1 = 7; // num 1
			type2 = 8; // num 2
			type3 = 4; // num 4
		}
		else
		if(type == 3) // RR
		{
			type1 = 9; // num 3
			type2 = 8; // num 2
			type3 = 6; // num 6
		}

		updatevariables();
	}

	public void remove()
	{
		getCar_LocalVersion();
		if(the_car)
			the_car.PneumaticSA.removeElement(this);
	}

	public void falloff()
	{
		remove();
	}

	public void Use( int action, int pos )
	{
		if( pos == type1 || pos == type2 || pos == type3 || pos == 5)
		{
			if(mode2 > 0)
			{
				action = 1-action;
			}

			if( mode != action )
			{
				if(mode == 0)
				{
					Use_Down_SFX.play( getPos(), 0.0, System.timeWarp(-1), 1.0, 0 ); // SFX Stop

					Cur_length = Down_length;
					mode = 1;
					update();
				}
				else
				if(mode == 1)
				{
					Use_Up_SFX.play( getPos(), 0.0, System.timeWarp(-1), 1.0, 0 ); // SFX Stop

					Cur_length = Up_length;
					mode = 0;
					update();
				}
			}
		}
		else
		if( pos == 10 )
		{
			mode2 = 1-mode2;

			Use( 0, 5 ); // Set new position
		}
	}
		
	public void updatevariables()
	{
		whl = getWheel();

		if (whl)
		{
			if(mode == 0) rebound_factor = 1.2;
			else rebound_factor = 0.2;

			whl.setDamping(damping, damping*rebound_factor);

			whl.setRestLen(Cur_length);
			whl.setMinLen(Cur_length-0.05);
			whl.setMaxLen(Cur_length+0.05);
		}
	}

	public void update()
	{
		if( !whl )
			whl = getWheel();

		if (whl)
		{
			if(mode == 0) rebound_factor = 1.2;
			else rebound_factor = 0.2;

			whl.setDamping(damping, damping*rebound_factor);

			whl.setRestLen(Cur_length);
			whl.setMinLen(Cur_length-0.05);
			whl.setMaxLen(Cur_length+0.05);
		}

		//if( !the_car )
		//	getCar_LocalVersion();

		//if (the_car)
			//the_car.forceUpdate();

		if(GameLogic.player.car)
			GameLogic.player.car.wakeUp();
	}

	//---------tuning
	public int isTuneable()
	{
		return (1);
	}

	// backup values //
	int old_type;
	float	old_damping;
	float	old_Up, temp_Up;
	float	old_Down, temp_Down;

	public void buildTuningMenu( Menu m )
	{
		old_type = type;
		old_damping = damping;
		old_Up = Up_length;
		old_Down = Down_length;

		temp_Up = Up_length;
		temp_Down = Down_length;

		m.addItem( "Position in the car:",	1, type, TypeText, null );
		m.addSeparator();
		m.addItem( "Static length:",	2, Up_length, min_length, max_length, 31, null ).changeVLabelText( Float.toString(Up_length*1000.0, "%1.0f mm (") + Float.toString(Up_length*100.0/2.54, "%1.1f inch)") );
		m.addItem( "Action length:",	3, Down_length, min_length, max_length, 31, null ).changeVLabelText( Float.toString(Down_length*1000.0, "%1.0f mm (") + Float.toString(Down_length*100.0/2.54, "%1.1f inch)") );
		m.addSeparator();
		m.addItem( "Bound damping:",	4, damping, min_damping, max_damping, 21, null ).printValue("%1.0f N/m/s");
		m.addSeparator();
		m.addItem( "CHECK STATIC LENGHT", 5);
		m.addItem( "CHECK ACTION LENGHT", 6);
		m.addSeparator();
		m.addItem( "Set this parameters for all in the car", 7);

	}

	public void endTuningSession( int cancelled )
	{
		if( cancelled )
		{
			type = old_type;
			damping = old_damping;
			Up_length = old_Up;
			Down_length = old_Down;
		}
		else
		{
			if (old_damping != damping) GameLogic.spendTime(2*60);
			if (old_type != type) GameLogic.spendTime(1*60);
			if (old_Up != Up_length) GameLogic.spendTime(2*60);
			if (old_Down != Down_length) GameLogic.spendTime(2*60);

			if(type == 0) // FL
			{
				type1 = 1; // num 7
				type2 = 2; // num 8
				type3 = 4; // num 4
			}
			else
			if(type == 1) // FR
			{
				type1 = 3; // num 9
				type2 = 2; // num 8
				type3 = 6; // num 6
			}
			else
			if(type == 2) // RL
			{
				type1 = 7; // num 1
				type2 = 8; // num 2
				type3 = 4; // num 4
			}
			else
			if(type == 3) // RR
			{
				type1 = 9; // num 3
				type2 = 8; // num 2
				type3 = 6; // num 6
			}

			update();
		}
	}

	public void handleMessage( Event m )
	{
		if( m.cmd == 1 )
		{
			type = ((Slider)m.gadget).osd.multiValue;
		}
		else
		if( m.cmd == 2 )
		{
			temp_Up = ((Slider)m.gadget).value;
			Up_length = temp_Up;
			
			((Slider)m.gadget).changeVLabelText( Float.toString(Up_length*1000.0, "%1.0f mm (") + Float.toString(Up_length*100.0/2.54, "%1.1f inch)") );

			if(Up_length > Down_length)
				m.gadget.osd.findGadget( this, 3 ).setValue( Up_length );
		}
		else
		if( m.cmd == 3 )
		{
			temp_Down = ((Slider)m.gadget).value;
			Down_length = temp_Down;

			((Slider)m.gadget).changeVLabelText( Float.toString(Down_length*1000.0, "%1.0f mm (") + Float.toString(Down_length*100.0/2.54, "%1.1f inch)") );

			if(Down_length < Up_length)
				m.gadget.osd.findGadget( this, 2 ).setValue( Down_length );
		}
		else
		if( m.cmd == 4 )
		{
			damping = ((Slider)m.gadget).value;
		}
		if( m.cmd == 5 )
		{
			System.timeWarp(1.0);
			Cur_length = Up_length;

			if(mode != 0)
				new SfxRef(MW_Mod:0x0072r).play(); // SFX

			mode = 0;
			the_car.forceUpdate();
			update();
		}
		else
		if( m.cmd == 6 )
		{
			System.timeWarp(1.0);
			Cur_length = Down_length;

			if(mode != 1)
				new SfxRef(MW_Mod:0x0071r).play(); // SFX

			mode = 1;
			the_car.forceUpdate();
			update();
		}
		else
		if( m.cmd == 7 )
		{
			getCar_LocalVersion();
			if( the_car )
			{
				for( int i=0; i<=the_car.PneumaticSA.size()-1; i++ )
				{
					if( the_car.PneumaticSA.elementData[i] && the_car.PneumaticSA.elementData[i] instanceof Pneumatic_ShockAbsorber )
					{
						the_car.PneumaticSA.elementData[i].damping = damping;
						the_car.PneumaticSA.elementData[i].Up_length = Up_length;
						the_car.PneumaticSA.elementData[i].Down_length = Down_length;
						the_car.PneumaticSA.elementData[i].Cur_length = Cur_length;
						the_car.PneumaticSA.elementData[i].Use( mode, 5 );
						
						the_car.forceUpdate();
						update();
					}
				}
			}
		}
	}
	//---------tuning
}