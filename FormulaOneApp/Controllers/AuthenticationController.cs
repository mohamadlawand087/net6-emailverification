using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FormulaOneApp.Configurations;
using FormulaOneApp.Models;
using FormulaOneApp.Models.DTOs;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using RestSharp;
using RestSharp.Authenticators;

namespace FormulaOneApp.Controllers;

[Route("api/[controller]")] // api/authentication
[ApiController]
public class AuthenticationController : ControllerBase
{
    private readonly UserManager<IdentityUser> _userManager;

    private readonly IConfiguration _configuration;
    //private readonly JwtConfig _jwtConfig;

    public AuthenticationController(
        UserManager<IdentityUser> userManager,
        IConfiguration configuration
        //JwtConfig jwtConfig
        )
    {
        _userManager = userManager;
        //_jwtConfig = jwtConfig;
        _configuration = configuration;
    }

    [HttpPost]
    [Route("Register")]
    public async Task<IActionResult> Register([FromBody] UserRegistrationRequestDto requestDto)
    {
        // Validate the incoming request
        if (ModelState.IsValid)
        {
            // We need to check if the email already exist
            var user_exist = await _userManager.FindByEmailAsync(requestDto.Email);

            if (user_exist != null)
            {
                return BadRequest(new AuthResult()
                {
                    Result = false,
                    Errors = new List<string>()
                    {
                        "Email already exist"
                    }
                });
            }
            
            // create a user
            var new_user = new IdentityUser()
            {
                Email = requestDto.Email,
                UserName = requestDto.Email,
                EmailConfirmed = false
            };

            var is_created = await _userManager.CreateAsync(new_user, requestDto.Password);

            if (is_created.Succeeded)
            {
                var code = await _userManager.GenerateEmailConfirmationTokenAsync(new_user);
                   
                var email_body = "Please confirm your email address <a href=\"#URL#\">Click here </a> ";
                
                // https://localhost:8080/authentication/verifyemail/userid=sdas&code=dasdasd 
                var callback_url = Request.Scheme + "://" + Request.Host + Url.Action("ConfirmEmail", "Authentication",
                    new {userId = new_user.Id, code = code});

                var body = email_body.Replace("#URL#",
                    callback_url);

                // SEND EMAIL
                var result = SendEmail(body, new_user.Email);
                
                if (result)
                    return Ok("Please verify your email, through the verification email we have just sent.");
                
                return Ok("Please request an email verification link");
                // Generate the token
                //var token = GenerateJwtToken(new_user);

                // return Ok(new AuthResult()
                // {
                //     Result = true,
                //     Token = token
                // });
            }

            return BadRequest(new AuthResult()
            {
                Errors = new List<string>()
                {
                    "Server error"
                },
                Result = false
            });
        }

        return BadRequest();
    }

    [Route("ConfirmEmail")]
    [HttpGet]
    public async Task<IActionResult> ConfirmEmail(string userId, string code)
    {
        if (userId == null || code == null)
        {
            return BadRequest(new AuthResult()
            {
                Errors = new List<string>()
                {
                    "Invalid email confirmation url"
                }
            });
        }

        var user = await _userManager.FindByIdAsync(userId);

        if (user == null)
        {
            return BadRequest(new AuthResult()
            {
                Errors = new List<string>()
                {
                    "Invalid email parameter"
                }
            });
        }

        //code =  Encoding.UTF8.GetString(Convert.FromBase64String(code));
        var result = await _userManager.ConfirmEmailAsync(user, code);
        var status = result.Succeeded
            ? "Thank you for confirming your mail"
            : "Your email is not confirmed, please try again later";
        
        return Ok(status);
    }

    [Route("Login")]
    [HttpPost]
    public async Task<IActionResult> Login([FromBody] UserLoginRequestDto loginRequest)
    {
        if (ModelState.IsValid)
        {
            // Check if the user exist
            var existing_user = await _userManager.FindByEmailAsync(loginRequest.Email);

            if (existing_user == null)
                return BadRequest(new AuthResult()
                {
                    Errors = new List<string>()
                    {
                        "Invalid payload"
                    },
                    Result = false
                });
            
            if(!existing_user.EmailConfirmed)
                return BadRequest(new AuthResult()
                {
                    Errors = new List<string>()
                    {
                        "Email needs to be confirmed"
                    },
                    Result = false
                });

            var isCorrect = await _userManager.CheckPasswordAsync(existing_user, loginRequest.Password);

            if (!isCorrect)
                return BadRequest(new AuthResult()
                {
                    Errors = new List<string>()
                    {
                        "Invalid credentials"
                    },
                    Result = false
                });

            var jwtToken = GenerateJwtToken(existing_user);

            return Ok(new AuthResult()
            {
                Token = jwtToken,
                Result = true
            });
        }

        return BadRequest(new AuthResult()
        {
            Errors = new List<string>()
            {
                "Invalid payload"
            },
            Result = false
        });
    }

    private string GenerateJwtToken(IdentityUser user)
    {
        var jwtTokenHandler = new JwtSecurityTokenHandler();

        var key = Encoding.UTF8.GetBytes(_configuration.GetSection("JwtConfig:Secret").Value);
        
        // Token descriptor
        var tokenDescriptor = new SecurityTokenDescriptor() 
        {
            Subject = new ClaimsIdentity(new []
            {
                new Claim("Id", user.Id),
                new Claim(JwtRegisteredClaimNames.Sub, user.Email),
                new Claim(JwtRegisteredClaimNames.Email, value:user.Email),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim(JwtRegisteredClaimNames.Iat, DateTime.Now.ToUniversalTime().ToString())
            }),
            
            Expires = DateTime.Now.AddHours(1),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256)
        };

        var token = jwtTokenHandler.CreateToken(tokenDescriptor);
        return jwtTokenHandler.WriteToken(token);
    }

    private bool SendEmail(string body, string email)
    {
        // Create client
        var client = new RestClient("https://api.mailgun.net/v3");

        var request = new RestRequest("", Method.Post);

        client.Authenticator = 
            new HttpBasicAuthenticator("api", _configuration.GetSection("EmailConfig:API_KEY").Value);

        request.AddParameter("domain", "sandbox6f6517f9e5da43cab8f3d5897b327d9f.mailgun.org", ParameterType.UrlSegment);
        request.Resource = "{domain}/messages";
        request.AddParameter("from", "Mailgun Sandbox <postmaster@sandbox6f6517f9e5da43cab8f3d5897b327d9f.mailgun.org>");
        request.AddParameter("to", "hello@mohamadlawand.com");
        request.AddParameter("subject", "Email Verification ");
        request.AddParameter("text", body);
        request.Method = Method.Post;

        var response = client.Execute(request);

        return response.IsSuccessful;
    }
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
}