sed -i.bak 's/RUN apt-get update .*/RUN apt-get update \&\& apt-get install -y libgssapi-krb5-2 \&\& rm -rf \/var\/lib\/apt\/lists\/*/g' src/AsistenteAyuntamiento.ApiService/Dockerfile
sed -i.bak 's/RUN apt-get update .*/RUN apt-get update \&\& apt-get install -y libgssapi-krb5-2 \&\& rm -rf \/var\/lib\/apt\/lists\/*/g' src/AsistenteAyuntamiento.Worker/Dockerfile
